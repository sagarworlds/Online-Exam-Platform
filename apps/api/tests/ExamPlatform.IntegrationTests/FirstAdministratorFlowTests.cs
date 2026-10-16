using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ExamPlatform.Modules.Admin.Infrastructure;
using ExamPlatform.Modules.Identity.Application.Commands;
using ExamPlatform.Modules.Identity.Application.Exceptions;
using ExamPlatform.Modules.Identity.Domain;
using ExamPlatform.Modules.Identity.Domain.Rbac;
using ExamPlatform.Modules.Identity.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// The first administrator of a new environment (ADR 0005), from nothing to a signed-in Super Admin, with the real database,
/// the real password-reset flow and the real second-factor sign-in. This class needs a database with no Super Admin, so it
/// holds one test and every other test of the feature lives in its own class.
/// </summary>
public sealed class FirstAdministratorFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string AdminEmail = "first.admin@tests.local";
    private const string ChosenPassword = "correct horse battery staple 42";

    [Fact]
    public async Task TheAddressOwner_SetsAPasswordFromTheEmailedLink_AndSignsInAsSuperAdminThroughTwoFactor()
    {
        Guid adminId;
        using (var scope = factory.Services.CreateScope())
        {
            adminId = await scope.ServiceProvider.GetRequiredService<ProvisionFirstAdministratorHandler>()
                .HandleAsync(new ProvisionFirstAdministratorCommand(AdminEmail), CancellationToken.None);
        }

        using var client = factory.CreateClient();

        // Nobody knows the password the account was created with, so nothing signs in before the link is used.
        var guess = await client.PostAsJsonAsync("/v1/auth/login", new { email = AdminEmail, password = "anything at all 12345" });
        Assert.Equal(HttpStatusCode.Unauthorized, guess.StatusCode);

        // What the address received is the ordinary password-reset code, "link id:secret".
        var parts = factory.OtpSender.GetLastCode(AdminEmail).Split(':');
        var reset = await client.PostAsJsonAsync("/v1/auth/password-reset/reset", new
        {
            passwordResetTokenId = Guid.Parse(parts[0]),
            token = parts[1],
            newPassword = ChosenPassword,
        });
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);

        // A Super Admin signs in with the password and then a code sent to the same address (FR-3).
        var login = await client.PostAsJsonAsync("/v1/auth/login", new { email = AdminEmail, password = ChosenPassword });
        login.EnsureSuccessStatusCode();
        var pending = await login.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(pending.GetProperty("requiresTwoFactor").GetBoolean());

        var verify = await client.PostAsJsonAsync("/v1/auth/otp/verify", new
        {
            otpChallengeId = pending.GetProperty("otpChallengeId").GetGuid(),
            code = factory.OtpSender.GetLastCode(AdminEmail),
        });
        verify.EnsureSuccessStatusCode();
        var accessToken = (await verify.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/v1/admin/audit-logs")).StatusCode);

        using var check = factory.Services.CreateScope();
        var admin = await check.ServiceProvider.GetRequiredService<IdentityDbContext>().Users
            .Include(u => u.Roles).SingleAsync(u => u.Id == adminId);
        Assert.Equal([RbacCatalog.RoleNames.SuperAdmin], admin.Roles.Select(r => r.Name));
        Assert.Equal(UserStatus.Active, admin.Status);

        // The record of it names the account by id and never holds the address.
        var entry = await check.ServiceProvider.GetRequiredService<AdminDbContext>().AuditLogs.AsNoTracking()
            .SingleAsync(a => a.EntityId == adminId.ToString() && a.Action == "Identity.FirstAdministratorProvisioned");
        Assert.Null(entry.ActorUserId);
        Assert.DoesNotContain(AdminEmail, string.Join(' ', entry.Metadata.Values), StringComparison.OrdinalIgnoreCase);

        // From now on the job refuses: it gives an environment its first administrator, never another one.
        using var again = factory.Services.CreateScope();
        await Assert.ThrowsAsync<FirstAdministratorAlreadyExistsError>(() =>
            again.ServiceProvider.GetRequiredService<ProvisionFirstAdministratorHandler>()
                .HandleAsync(new ProvisionFirstAdministratorCommand("someone.else@tests.local"), CancellationToken.None));
    }
}

/// <summary>
/// The refusals that depend on what is already in the database. Each is its own check against a database where a Super Admin
/// was created some other way, as in an environment that was set up by copying a local database.
/// </summary>
public sealed class FirstAdministratorRefusalTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task WhenASuperAdminWasCreatedAnotherWay_TheJobRefuses_AndSendsNothing()
    {
        await factory.SignInAsAsync("SuperAdmin");
        const string address = "late.arrival@tests.local";

        using var scope = factory.Services.CreateScope();
        await Assert.ThrowsAsync<FirstAdministratorAlreadyExistsError>(() =>
            scope.ServiceProvider.GetRequiredService<ProvisionFirstAdministratorHandler>()
                .HandleAsync(new ProvisionFirstAdministratorCommand(address), CancellationToken.None));

        Assert.False(factory.OtpSender.HasSentTo(address));
        Assert.False(await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Users.AnyAsync(u => u.Email == address));
    }
}
