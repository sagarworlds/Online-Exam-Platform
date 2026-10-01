using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ExamPlatform.Modules.Consent.Contracts;
using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain;
using ExamPlatform.Modules.Identity.Infrastructure;
using ExamPlatform.SharedKernel.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// Drives the full attempt-adjacent journey over real HTTP against the actual
/// Host: register → verify OTP → session supersede on a second login → consent
/// grant/withdraw → RBAC deny/allow → cross-module audit trail. One scenario,
/// not several independent facts, since each step's assertions depend on state
/// the previous step created — splitting it would just reintroduce that coupling
/// via shared fixture state instead of removing it.
/// </summary>
public sealed class AuthConsentAuditFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    // Mirrors the Host's own ConfigureHttpJsonOptions (Program.cs): case-insensitive
    // property matching for the camelCase wire format, and enums as strings — without
    // this, deserializing ConsentStatusDto's "purpose": "PrivacyNotice" would fail,
    // since the response is no longer the numeric form System.Text.Json expects by default.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    [Fact]
    public async Task FullJourney_RegisterLoginConsentAndAudit_Succeeds()
    {
        var client = factory.CreateClient();
        const string email = "journey-candidate@example.com";

        // 1. Register a candidate and confirm via OTP.
        var registerResponse = await client.PostAsJsonAsync("/v1/auth/register", new
        {
            email,
            phoneNumber = (string?)null,
            dateOfBirth = "2005-06-15",
            displayName = "Journey Candidate",
            otpChannel = "Email",
        });
        registerResponse.EnsureSuccessStatusCode();
        var registerBody = await registerResponse.Content.ReadFromJsonAsync<OtpChallengeResponse>(JsonOptions);
        var registrationCode = factory.OtpSender.GetLastCode(email);

        var firstVerify = await client.PostAsJsonAsync("/v1/auth/otp/verify", new
        {
            otpChallengeId = registerBody!.OtpChallengeId,
            code = registrationCode,
        });
        firstVerify.EnsureSuccessStatusCode();
        var firstAuth = await firstVerify.Content.ReadFromJsonAsync<AuthResultResponse>(JsonOptions);

        // 2. The candidate can read their own profile.
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", firstAuth!.AccessToken);
        var profileResponse = await client.GetAsync("/v1/me/profile");
        profileResponse.EnsureSuccessStatusCode();
        var profile = await profileResponse.Content.ReadFromJsonAsync<ProfileResponse>(JsonOptions);
        Assert.Equal("Journey Candidate", profile!.DisplayName);
        var subjectId = profile.UserId;

        // 3. Logging in again supersedes the first session (FR-4) — verified at the
        // data level, since this slice does not build live per-request revocation checks.
        var secondRequest = await client.PostAsJsonAsync("/v1/auth/otp/request", new { channel = "Email", destination = email });
        secondRequest.EnsureSuccessStatusCode();
        var secondChallenge = await secondRequest.Content.ReadFromJsonAsync<OtpChallengeResponse>(JsonOptions);
        var secondCode = factory.OtpSender.GetLastCode(email);
        var secondVerify = await client.PostAsJsonAsync("/v1/auth/otp/verify", new
        {
            otpChallengeId = secondChallenge!.OtpChallengeId,
            code = secondCode,
        });
        secondVerify.EnsureSuccessStatusCode();
        var secondAuth = await secondVerify.Content.ReadFromJsonAsync<AuthResultResponse>(JsonOptions);

        using (var scope = factory.Services.CreateScope())
        {
            var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var firstSession = await identityDb.Set<UserSession>().SingleAsync(s => s.Id == firstAuth.SessionId);
            Assert.Equal(SessionRevocationReason.SupersededByNewLogin, firstSession.RevokedReason);
            var secondSession = await identityDb.Set<UserSession>().SingleAsync(s => s.Id == secondAuth!.SessionId);
            Assert.Null(secondSession.RevokedAtUtc);
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secondAuth!.AccessToken);

        // 4. Consent: grant, check status, withdraw, check status again.
        Guid noticeVersionId;
        using (var scope = factory.Services.CreateScope())
        {
            var consentDb = scope.ServiceProvider.GetRequiredService<ExamPlatform.Modules.Consent.Infrastructure.ConsentDbContext>();
            noticeVersionId = await consentDb.NoticeVersions
                .Where(n => n.Purpose == ExamPlatform.Modules.Consent.Domain.ConsentPurpose.PrivacyNotice)
                .Select(n => n.Id)
                .FirstAsync();
        }

        var statusBeforeGrant = await client.GetFromJsonAsync<ConsentStatusDto>(
            $"/v1/consent/status?subjectId={subjectId}&purpose=PrivacyNotice", JsonOptions);
        Assert.False(statusBeforeGrant!.IsActive);

        var grantResponse = await client.PostAsJsonAsync("/v1/consent/", new
        {
            subjectId,
            purpose = "PrivacyNotice",
            noticeVersionId,
        });
        grantResponse.EnsureSuccessStatusCode();
        var consentRecord = await grantResponse.Content.ReadFromJsonAsync<ConsentRecordDto>(JsonOptions);

        var statusAfterGrant = await client.GetFromJsonAsync<ConsentStatusDto>(
            $"/v1/consent/status?subjectId={subjectId}&purpose=PrivacyNotice", JsonOptions);
        Assert.True(statusAfterGrant!.IsActive);

        var withdrawResponse = await client.DeleteAsync($"/v1/consent/{consentRecord!.ConsentRecordId}");
        Assert.Equal(HttpStatusCode.NoContent, withdrawResponse.StatusCode);

        var statusAfterWithdraw = await client.GetFromJsonAsync<ConsentStatusDto>(
            $"/v1/consent/status?subjectId={subjectId}&purpose=PrivacyNotice", JsonOptions);
        Assert.False(statusAfterWithdraw!.IsActive);

        // 5. RBAC denies a candidate calling the admin audit log.
        var deniedAuditResponse = await client.GetAsync("/v1/admin/audit-logs");
        Assert.Equal(HttpStatusCode.Forbidden, deniedAuditResponse.StatusCode);

        // 6. Seed a SuperAdmin directly (arrange step — no endpoint exists to create one,
        // since invite/admin-provisioning is out of this slice's scope), then sign it in the
        // only way its role allows (FR-3: password, then a second-factor OTP) and confirm the
        // permission it carries lets it both assign a role and read the audit trail.
        const string adminEmail = "journey-admin@example.com";
        const string adminPassword = "journey-admin-password";
        using (var scope = factory.Services.CreateScope())
        {
            var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var nowUtc = scope.ServiceProvider.GetRequiredService<Clock>().UtcNow;
            var superAdminRole = await identityDb.Roles.Include(r => r.Permissions).SingleAsync(r => r.Name == "SuperAdmin");
            var admin = User.Register(adminEmail, null, new DateOnly(1990, 1, 1), "Journey Admin", nowUtc);
            admin.AssignRole(superAdminRole);
            admin.SetPasswordHash(scope.ServiceProvider.GetRequiredService<IPasswordHasher>().Hash(adminPassword));
            admin.Activate();
            await identityDb.Users.AddAsync(admin);
            await identityDb.SaveChangesAsync();
        }

        // The OTP-only path a candidate uses cannot sign a 2FA-required account in: it answers
        // as usual, but with a decoy challenge, and no code is ever sent to the admin.
        var adminOtpRequest = await client.PostAsJsonAsync("/v1/auth/otp/request", new { channel = "Email", destination = adminEmail });
        adminOtpRequest.EnsureSuccessStatusCode();
        Assert.False(factory.OtpSender.HasSentTo(adminEmail));

        var adminLogin = await client.PostAsJsonAsync("/v1/auth/login", new { email = adminEmail, password = adminPassword });
        adminLogin.EnsureSuccessStatusCode();
        var adminPending = await adminLogin.Content.ReadFromJsonAsync<AuthResultResponse>(JsonOptions);
        Assert.True(adminPending!.RequiresTwoFactor);
        Assert.Null(adminPending.AccessToken);
        Assert.NotNull(adminPending.OtpChallengeId);

        var adminVerify = await client.PostAsJsonAsync("/v1/auth/otp/verify", new
        {
            otpChallengeId = adminPending.OtpChallengeId,
            code = factory.OtpSender.GetLastCode(adminEmail),
        });
        adminVerify.EnsureSuccessStatusCode();
        var adminAuth = await adminVerify.Content.ReadFromJsonAsync<AuthResultResponse>(JsonOptions);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminAuth!.AccessToken);

        Guid teacherRoleId;
        using (var scope = factory.Services.CreateScope())
        {
            var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            teacherRoleId = await identityDb.Roles.Where(r => r.Name == "InstituteTeacher").Select(r => r.Id).FirstAsync();
        }

        var assignRoleResponse = await client.PostAsJsonAsync($"/v1/admin/users/{subjectId}/roles", new { roleId = teacherRoleId });
        Assert.Equal(HttpStatusCode.NoContent, assignRoleResponse.StatusCode);

        var allowedAuditResponse = await client.GetAsync("/v1/admin/audit-logs");
        Assert.Equal(HttpStatusCode.OK, allowedAuditResponse.StatusCode);
        var auditEntries = await allowedAuditResponse.Content.ReadFromJsonAsync<List<AuditLogEntryResponse>>(JsonOptions);

        Assert.Contains(auditEntries!, e => e.Action == "Consent.Withdrawn" && e.EntityId == consentRecord.ConsentRecordId.ToString());
        Assert.Contains(auditEntries!, e => e.Action == "Identity.RoleAssigned" && e.EntityId == subjectId.ToString());
    }

    [Fact]
    public async Task GetProfile_WithoutAToken_ReturnsUnauthorized()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/v1/me/profile");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed record OtpChallengeResponse(Guid OtpChallengeId);

    private sealed record AuthResultResponse(bool RequiresTwoFactor, string? AccessToken, Guid? SessionId, Guid? OtpChallengeId);

    private sealed record ProfileResponse(Guid UserId, string? Email, string? PhoneNumber, string DisplayName, string Status, IReadOnlyCollection<string> Roles);

    private sealed record AuditLogEntryResponse(Guid Id, DateTime OccurredAtUtc, Guid? ActorUserId, string? ActorRole, string Action, string EntityType, string EntityId, IReadOnlyDictionary<string, string> Metadata);
}
