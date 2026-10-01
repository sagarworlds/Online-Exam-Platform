using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ExamPlatform.Modules.Identity.Application;
using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain;
using ExamPlatform.Modules.Identity.Infrastructure;
using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// Drives the Identity module's security rules over real HTTP and a real database:
/// the OTP brute-force lockout, replay protection, supersession of older codes,
/// concurrency guards (FR-1, NFR-5), which accounts an OTP may sign in (FR-3), that a
/// token stops working as soon as its session ends, including by logging out (FR-4),
/// that a password reset enforces the password policy, ends every session and withdraws
/// older reset links (FR-3), and that malformed sign-up, sign-in and profile input gets
/// a typed 400 rather than a 500 (section 11).
/// Each test arranges its own user with a unique address, so the tests are independent
/// of each other and of the order they run in.
/// </summary>
public sealed class IdentitySecurityFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    // Long enough for the password policy and free of any test email's local part.
    private const string NewStrongPassword = "correct horse battery staple";

    [Fact]
    public async Task VerifyOtp_FiveWrongCodes_LocksTheChallengeEvenForTheCorrectCode()
    {
        using var client = factory.CreateClient();
        var (email, challengeId) = await RegisterCandidateAsync(client);
        var correctCode = factory.OtpSender.GetLastCode(email);
        var wrongCode = DifferentCode(correctCode);

        for (var i = 0; i < 5; i++)
        {
            await AssertProblemAsync(
                await VerifyOtpAsync(client, challengeId, wrongCode), HttpStatusCode.BadRequest, "otp_mismatch");
        }

        // The sixth try uses the real code and is still refused: the five failures were
        // saved, so the lockout holds across requests (it used to reset every request).
        await AssertProblemAsync(
            await VerifyOtpAsync(client, challengeId, correctCode), HttpStatusCode.TooManyRequests, "otp_attempts_exceeded");

        using var scope = factory.Services.CreateScope();
        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var challenge = await identityDb.OtpChallenges.SingleAsync(c => c.Id == challengeId);
        Assert.Equal(5, challenge.AttemptCount);
        Assert.False(challenge.IsConsumed);
    }

    [Fact]
    public async Task VerifyOtp_ReplayingAConsumedCode_IsRejected()
    {
        using var client = factory.CreateClient();
        var (email, challengeId) = await RegisterCandidateAsync(client);
        var code = factory.OtpSender.GetLastCode(email);

        var firstVerify = await VerifyOtpAsync(client, challengeId, code);
        Assert.Equal(HttpStatusCode.OK, firstVerify.StatusCode);

        await AssertProblemAsync(
            await VerifyOtpAsync(client, challengeId, code), HttpStatusCode.BadRequest, "otp_already_used");

        // The replay minted nothing: the only session is the one the first verify started.
        using var scope = factory.Services.CreateScope();
        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var user = await identityDb.Users.Include(u => u.Sessions).SingleAsync(u => u.Email == email);
        var firstAuth = await firstVerify.Content.ReadFromJsonAsync<JsonElement>();
        var session = Assert.Single(user.Sessions);
        Assert.Equal(firstAuth.GetProperty("sessionId").GetGuid(), session.Id);
    }

    [Fact]
    public async Task ParallelVerifies_OfOneChallenge_CannotBothConsumeIt()
    {
        using var client = factory.CreateClient();
        var (email, challengeId) = await RegisterCandidateAsync(client);
        var code = factory.OtpSender.GetLastCode(email);

        // Two requests that loaded the same challenge before either saved, as two
        // parallel POST /v1/auth/otp/verify calls with the right code would.
        using var firstScope = factory.Services.CreateScope();
        using var secondScope = factory.Services.CreateScope();
        var first = await LoadChallengeAsync(firstScope, challengeId);
        var second = await LoadChallengeAsync(secondScope, challengeId);
        var codeHash = firstScope.ServiceProvider.GetRequiredService<IOtpCodeGenerator>().Hash(code);
        var nowUtc = firstScope.ServiceProvider.GetRequiredService<Clock>().UtcNow;

        Assert.Equal(OtpVerificationOutcome.Verified, first.Verify(codeHash, nowUtc));
        await firstScope.ServiceProvider.GetRequiredService<IIdentityUnitOfWork>().SaveChangesAsync(CancellationToken.None);

        // The second copy is stale, so it still believes the code is unused; the xmin row
        // version is what stops it from consuming the challenge (and minting a session) again.
        Assert.Equal(OtpVerificationOutcome.Verified, second.Verify(codeHash, nowUtc));
        var conflict = await Assert.ThrowsAsync<ConcurrencyConflictError>(
            () => secondScope.ServiceProvider.GetRequiredService<IIdentityUnitOfWork>().SaveChangesAsync(CancellationToken.None));
        Assert.Equal(409, conflict.HttpStatusCode);
        Assert.IsType<DbUpdateConcurrencyException>(conflict.InnerException);
    }

    [Fact]
    public async Task RequestOtp_Twice_OnlyTheLatestCodeIsAccepted()
    {
        var candidate = await factory.SignInAsAsync("Candidate");
        using var client = factory.CreateClient();

        var firstChallengeId = await RequestOtpAsync(client, candidate.Email);
        var firstCode = factory.OtpSender.GetLastCode(candidate.Email);
        var secondChallengeId = await RequestOtpAsync(client, candidate.Email);
        var secondCode = factory.OtpSender.GetLastCode(candidate.Email);

        // The first code is refused even though it is right and unexpired: the second
        // request superseded it, so the attempt budget covers only one live code at a time.
        await AssertProblemAsync(
            await VerifyOtpAsync(client, firstChallengeId, firstCode), HttpStatusCode.BadRequest, "otp_superseded");

        var secondVerify = await VerifyOtpAsync(client, secondChallengeId, secondCode);
        Assert.Equal(HttpStatusCode.OK, secondVerify.StatusCode);

        using var scope = factory.Services.CreateScope();
        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var first = await identityDb.OtpChallenges.SingleAsync(c => c.Id == firstChallengeId);
        Assert.True(first.IsSuperseded);
        Assert.False(first.IsConsumed);
        Assert.Equal(0, first.AttemptCount);
        var second = await identityDb.OtpChallenges.SingleAsync(c => c.Id == secondChallengeId);
        Assert.True(second.IsConsumed);
        Assert.False(second.IsSuperseded);
    }

    [Fact]
    public async Task RequestOtp_ForStaffAccount_SendsNothing_AndPasswordPlusTwoFactorStillWorks()
    {
        const string password = "staff-account-password";
        var admin = await factory.SignInAsAsync("SuperAdmin", password: password);
        using var client = factory.CreateClient();

        // The OTP-only path answers a 2FA-required account like any other, but with a decoy:
        // no code is sent, and no code can complete it (FR-3).
        var decoyChallengeId = await RequestOtpAsync(client, admin.Email);
        Assert.False(factory.OtpSender.HasSentTo(admin.Email));
        await AssertProblemAsync(
            await VerifyOtpAsync(client, decoyChallengeId, "000000"), HttpStatusCode.BadRequest, "otp_mismatch");

        var loginResponse = await client.PostAsJsonAsync("/v1/auth/login", new { email = admin.Email, password });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var pending = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(pending.GetProperty("requiresTwoFactor").GetBoolean());

        var verifyResponse = await VerifyOtpAsync(
            client, pending.GetProperty("otpChallengeId").GetGuid(), factory.OtpSender.GetLastCode(admin.Email));
        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);
        var auth = await verifyResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrEmpty(auth.GetProperty("accessToken").GetString()));
    }

    [Fact]
    public async Task RequestOtp_ForUnknownDestination_Returns200WithChallengeIdAndSendsNothing()
    {
        var email = $"nobody-{Guid.NewGuid():N}@tests.local";
        using var client = factory.CreateClient();

        var challengeId = await RequestOtpAsync(client, email);

        Assert.False(factory.OtpSender.HasSentTo(email));

        // The decoy is a real, persisted challenge, so verifying it answers exactly like a
        // real one: wrong codes count, and the sixth try is locked out, never a 404.
        for (var i = 0; i < 5; i++)
        {
            await AssertProblemAsync(
                await VerifyOtpAsync(client, challengeId, "000000"), HttpStatusCode.BadRequest, "otp_mismatch");
        }

        await AssertProblemAsync(
            await VerifyOtpAsync(client, challengeId, "000000"), HttpStatusCode.TooManyRequests, "otp_attempts_exceeded");

        using var scope = factory.Services.CreateScope();
        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var challenge = await identityDb.OtpChallenges.SingleAsync(c => c.Id == challengeId);
        Assert.Null(challenge.UserId);
        Assert.Equal(email, challenge.Destination);
    }

    [Fact]
    public async Task RequestOtp_ForSuspendedAccount_SendsNothing()
    {
        var candidate = await factory.SignInAsAsync("Candidate");
        await SuspendAsync(candidate.UserId);
        using var client = factory.CreateClient();

        var challengeId = await RequestOtpAsync(client, candidate.Email);

        Assert.False(factory.OtpSender.HasSentTo(candidate.Email));
        using var scope = factory.Services.CreateScope();
        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var challenge = await identityDb.OtpChallenges.SingleAsync(c => c.Id == challengeId);
        Assert.Null(challenge.UserId);
    }

    [Fact]
    public async Task SuspendedUser_CannotCompleteOtpLogin()
    {
        var candidate = await factory.SignInAsAsync("Candidate");
        using var client = factory.CreateClient();

        // The code is requested while the account is still active, then the account is
        // suspended before the code is used.
        var challengeId = await RequestOtpAsync(client, candidate.Email);
        var code = factory.OtpSender.GetLastCode(candidate.Email);
        await SuspendAsync(candidate.UserId);

        await AssertProblemAsync(
            await VerifyOtpAsync(client, challengeId, code), HttpStatusCode.Forbidden, "account_locked");

        using var scope = factory.Services.CreateScope();
        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var user = await identityDb.Users.Include(u => u.Sessions).SingleAsync(u => u.Id == candidate.UserId);
        var session = Assert.Single(user.Sessions);
        Assert.Equal(SessionRevocationReason.AccountSuspended, session.RevokedReason);
    }

    [Fact]
    public async Task GetOutstanding_ReturnsOnlyStillVerifiableChallengesForTheDestinationAndPurpose()
    {
        var destination = $"outstanding-{Guid.NewGuid():N}@tests.local";
        DateTime nowUtc;
        Guid liveChallengeId;
        Guid expiringNowChallengeId;

        using (var arrangeScope = factory.Services.CreateScope())
        {
            // Whole seconds, so the exact-expiry row below survives Postgres's microsecond
            // timestamp precision unchanged and the boundary comparison is exact.
            var clockNow = arrangeScope.ServiceProvider.GetRequiredService<Clock>().UtcNow;
            nowUtc = new DateTime(clockNow.Ticks - (clockNow.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);
            var validity = TimeSpan.FromMinutes(10);
            OtpChallenge Issue(string to, OtpPurpose purpose, DateTime issuedAt) =>
                OtpChallenge.Issue(null, OtpChannel.Email, to, "code-hash", purpose, issuedAt, validity);

            var live = Issue(destination, OtpPurpose.Login, nowUtc);
            // Expires exactly now: Verify still accepts it, so it is still outstanding.
            var expiringNow = Issue(destination, OtpPurpose.Login, nowUtc - validity);
            var consumed = Issue(destination, OtpPurpose.Login, nowUtc);
            Assert.Equal(OtpVerificationOutcome.Verified, consumed.Verify("code-hash", nowUtc));
            var superseded = Issue(destination, OtpPurpose.Login, nowUtc);
            superseded.Supersede(nowUtc);
            var expired = Issue(destination, OtpPurpose.Login, nowUtc.AddMinutes(-20));
            var otherPurpose = Issue(destination, OtpPurpose.TwoFactorStep, nowUtc);
            var otherDestination = Issue($"other-{Guid.NewGuid():N}@tests.local", OtpPurpose.Login, nowUtc);

            var identityDb = arrangeScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            identityDb.OtpChallenges.AddRange(live, expiringNow, consumed, superseded, expired, otherPurpose, otherDestination);
            await identityDb.SaveChangesAsync();
            liveChallengeId = live.Id;
            expiringNowChallengeId = expiringNow.Id;
        }

        // A fresh scope, so the query reads the database rather than the change tracker.
        using var scope = factory.Services.CreateScope();
        var outstanding = await scope.ServiceProvider.GetRequiredService<IOtpChallengeRepository>()
            .GetOutstandingAsync(destination, OtpPurpose.Login, nowUtc, CancellationToken.None);

        Assert.Equal(
            new[] { liveChallengeId, expiringNowChallengeId }.Order(),
            outstanding.Select(c => c.Id).Order());
    }

    [Theory]
    [InlineData("Fax")]
    [InlineData("5")]
    [InlineData("Email,Sms")]
    [InlineData(null)]
    public async Task RequestOtp_WithUnknownChannel_Returns400(string? channel)
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/v1/auth/otp/request", new { channel, destination = $"channel-{Guid.NewGuid():N}@tests.local" });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "invalid_otp_channel");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task RequestOtp_WithBlankDestination_Returns400(string? destination)
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/v1/auth/otp/request", new { channel = "Email", destination });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "invalid_contact");
    }

    [Fact]
    public async Task RequestOtp_WithDestinationLongerThanAChallengeStores_Returns400()
    {
        using var client = factory.CreateClient();
        var destination = new string('a', OtpChallenge.MaxDestinationLength + 1 - "@tests.local".Length) + "@tests.local";

        var response = await client.PostAsJsonAsync("/v1/auth/otp/request", new { channel = "Email", destination });

        // Used to reach the decoy challenge insert and fail there (varchar(320)) as a 500.
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "invalid_contact");
    }

    [Fact]
    public async Task Register_WithEmailLongerThanStored_Returns400()
    {
        using var client = factory.CreateClient();
        var email = new string('a', User.MaxEmailLength + 1 - "@tests.local".Length) + "@tests.local";

        var response = await PostRegisterAsync(client, email, phoneNumber: null, otpChannel: "Email");

        // Used to reach the user insert and fail there (varchar(320)) as a 500.
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "invalid_contact");
    }

    [Fact]
    public async Task Register_TwoPhoneOnlyAccountsWithBlankEmails_AreBothCreated()
    {
        using var client = factory.CreateClient();
        var firstPhone = UniquePhoneNumber();
        var secondPhone = UniquePhoneNumber();

        var first = await PostRegisterAsync(client, email: "", firstPhone, otpChannel: "Sms");
        var second = await PostRegisterAsync(client, email: "", secondPhone, otpChannel: "Sms");

        // A blank email used to be stored as "", so the second account collided with the
        // first on the unique email index (which only skips nulls) and failed as a 500.
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        using var scope = factory.Services.CreateScope();
        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var emails = await identityDb.Users
            .Where(u => u.PhoneNumber == firstPhone || u.PhoneNumber == secondPhone)
            .Select(u => u.Email)
            .ToListAsync();
        Assert.Equal(2, emails.Count);
        Assert.All(emails, Assert.Null);
    }

    [Theory]
    [InlineData("Fax")]
    [InlineData("1")]
    public async Task Register_WithInvalidChannel_Returns400(string otpChannel)
    {
        using var client = factory.CreateClient();
        var email = $"register-{Guid.NewGuid():N}@tests.local";

        var response = await PostRegisterAsync(client, email, phoneNumber: null, otpChannel);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "invalid_otp_channel");
        await AssertNoAccountAsync(email);
    }

    [Fact]
    public async Task Register_WithEmailChannelButOnlyPhone_Returns400()
    {
        using var client = factory.CreateClient();
        var phoneNumber = UniquePhoneNumber();

        var response = await PostRegisterAsync(client, email: null, phoneNumber, otpChannel: "Email");

        // Used to reach the challenge insert with a null destination and fail as a 500.
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "contact_channel_mismatch");
        using var scope = factory.Services.CreateScope();
        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.False(await identityDb.Users.AnyAsync(u => u.PhoneNumber == phoneNumber));
    }

    [Fact]
    public async Task Register_WithoutDateOfBirth_Returns400InvalidDateOfBirth()
    {
        using var client = factory.CreateClient();
        var omittedEmail = $"no-dob-{Guid.NewGuid():N}@tests.local";
        var nullEmail = $"null-dob-{Guid.NewGuid():N}@tests.local";

        // Compliance: an omitted date used to bind to 0001-01-01, which reads as an adult,
        // so leaving it out skipped minor detection and guardian consent (FR-43).
        var omitted = await client.PostAsJsonAsync("/v1/auth/register", new
        {
            email = omittedEmail,
            displayName = "Security Candidate",
            otpChannel = "Email",
        });
        var explicitNull = await PostRegisterAsync(client, nullEmail, phoneNumber: null, "Email", dateOfBirth: null);

        await AssertProblemAsync(omitted, HttpStatusCode.BadRequest, "invalid_date_of_birth");
        await AssertProblemAsync(explicitNull, HttpStatusCode.BadRequest, "invalid_date_of_birth");
        await AssertNoAccountAsync(omittedEmail);
        await AssertNoAccountAsync(nullEmail);
    }

    [Fact]
    public async Task Register_WithFutureDateOfBirth_Returns400()
    {
        using var client = factory.CreateClient();
        var email = $"future-dob-{Guid.NewGuid():N}@tests.local";
        var utcToday = DateOnly.FromDateTime(factory.Services.GetRequiredService<Clock>().UtcNow);
        var dateOfBirth = utcToday.AddDays(2).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var response = await PostRegisterAsync(client, email, phoneNumber: null, "Email", dateOfBirth);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "invalid_date_of_birth");
        await AssertNoAccountAsync(email);
    }

    public static TheoryData<string?> InvalidDisplayNames => new()
    {
        null,
        "",
        "   ",
        new string('a', User.MaxDisplayNameLength + 1),
    };

    [Theory]
    [MemberData(nameof(InvalidDisplayNames))]
    public async Task UpdateProfile_WithInvalidDisplayName_Returns400(string? displayName)
    {
        var candidate = await factory.SignInAsAsync("Candidate");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", candidate.AccessToken);

        var response = await client.PutAsJsonAsync("/v1/me/profile", new { displayName });

        // Used to reach the database and fail there (not-null or varchar(200)) as a 500.
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "invalid_display_name");
        Assert.Equal("Test Candidate", await GetDisplayNameAsync(client));
    }

    [Fact]
    public async Task UpdateProfile_StoresTheTrimmedDisplayName()
    {
        var candidate = await factory.SignInAsAsync("Candidate");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", candidate.AccessToken);

        var response = await client.PutAsJsonAsync("/v1/me/profile", new { displayName = "  Asha Rao  " });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("Asha Rao", await GetDisplayNameAsync(client));
    }

    [Fact]
    public async Task VerifyOtp_WithoutCode_CountsAsAWrongCode()
    {
        using var client = factory.CreateClient();
        var (_, challengeId) = await RegisterCandidateAsync(client);

        var response = await client.PostAsJsonAsync("/v1/auth/otp/verify", new { otpChallengeId = challengeId });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "otp_mismatch");
        using var scope = factory.Services.CreateScope();
        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var challenge = await identityDb.OtpChallenges.SingleAsync(c => c.Id == challengeId);
        Assert.Equal(1, challenge.AttemptCount);
    }

    [Fact]
    public async Task SupersededSessionToken_IsRejectedWith401SessionSuperseded()
    {
        var candidate = await factory.SignInAsAsync("Candidate");
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await GetProfileAsync(client, candidate.AccessToken)).StatusCode);

        // A second login (FR-4) supersedes the first session.
        var challengeId = await RequestOtpAsync(client, candidate.Email);
        var verifyResponse = await VerifyOtpAsync(client, challengeId, factory.OtpSender.GetLastCode(candidate.Email));
        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);
        var newToken = (await verifyResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;

        var oldTokenResponse = await GetProfileAsync(client, candidate.AccessToken);
        await AssertProblemAsync(oldTokenResponse, HttpStatusCode.Unauthorized, "session_superseded");
        Assert.Contains("error=\"invalid_token\"", oldTokenResponse.Headers.WwwAuthenticate.ToString());
        Assert.Equal(HttpStatusCode.OK, (await GetProfileAsync(client, newToken)).StatusCode);

        // A refused token only makes the caller anonymous: endpoints that need no sign-in,
        // such as signing in again, still answer a client that keeps sending it.
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", candidate.AccessToken);
        await RequestOtpAsync(client, candidate.Email);
    }

    [Fact]
    public async Task TokenWithoutSessionId_IsRejected()
    {
        var candidate = await factory.SignInAsAsync("Candidate");
        using var client = factory.CreateClient();

        // Correctly signed and unexpired, and its sub names a real user, but it names no session.
        var response = await GetProfileAsync(client, TestJwtTokenBuilder.GenerateCandidateToken(candidate.UserId));

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "session_unknown");
    }

    [Fact]
    public async Task TokenForAnotherUsersSession_IsRejected()
    {
        var candidate = await factory.SignInAsAsync("Candidate");
        var other = await factory.SignInAsAsync("Candidate");
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var users = services.GetRequiredService<IUserRepository>();
        var user = await users.GetByIdAsync(candidate.UserId, CancellationToken.None);
        var otherSession = (await users.GetByIdAsync(other.UserId, CancellationToken.None))!.Sessions.Single();
        using var client = factory.CreateClient();

        // Minted with the app's own key for a live session, so only the sid/sub pairing can catch it.
        var token = services.GetRequiredService<ITokenGenerator>().GenerateAccessToken(user!, otherSession);

        await AssertProblemAsync(await GetProfileAsync(client, token), HttpStatusCode.Unauthorized, "session_unknown");
    }

    [Fact]
    public async Task SuspendingAUser_InvalidatesTheirExistingToken()
    {
        var candidate = await factory.SignInAsAsync("Candidate");
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await GetProfileAsync(client, candidate.AccessToken)).StatusCode);

        await SuspendAsync(candidate.UserId);

        await AssertProblemAsync(
            await GetProfileAsync(client, candidate.AccessToken), HttpStatusCode.Unauthorized, "account_locked");
    }

    // One minute past expiry is still inside the bearer handler's default five-minute clock
    // skew, so the session check refuses it; an hour past fails the token's own lifetime
    // check first. Both must tell the client the same thing.
    [Theory]
    [InlineData(1)]
    [InlineData(60)]
    public async Task ExpiredSessionToken_IsRejectedWith401SessionExpired(int minutesSinceExpiry)
    {
        var candidate = await factory.SignInAsAsync("Candidate");
        var token = await StartPastSessionAsync(candidate.UserId, TimeSpan.FromMinutes(minutesSinceExpiry));
        using var client = factory.CreateClient();

        await AssertProblemAsync(await GetProfileAsync(client, token), HttpStatusCode.Unauthorized, "session_expired");
    }

    [Fact]
    public async Task Logout_RevokesTheSession_AndTheTokenStopsWorking()
    {
        var candidate = await factory.SignInAsAsync("Candidate");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", candidate.AccessToken);

        var logoutResponse = await client.PostAsync("/v1/auth/logout", content: null);

        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);
        await AssertProblemAsync(
            await GetProfileAsync(client, candidate.AccessToken), HttpStatusCode.Unauthorized, "session_revoked");

        using (var scope = factory.Services.CreateScope())
        {
            var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var session = await identityDb.Set<UserSession>().SingleAsync(s => s.Id == candidate.SessionId);
            Assert.Equal(SessionRevocationReason.LoggedOut, session.RevokedReason);
            Assert.NotNull(session.RevokedAtUtc);
        }

        // The ended session's token cannot even log out again.
        await AssertProblemAsync(
            await client.PostAsync("/v1/auth/logout", content: null), HttpStatusCode.Unauthorized, "session_revoked");
    }

    [Fact]
    public async Task Logout_WithoutToken_Returns401()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/v1/auth/logout", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PasswordReset_WithWeakPassword_Returns400WeakPassword()
    {
        var admin = await factory.SignInAsAsync("SuperAdmin", password: "the-original-password");
        using var client = factory.CreateClient();
        var link = await RequestPasswordResetAsync(client, admin.Email);

        await AssertProblemAsync(
            await ResetPasswordAsync(client, link, "too-short"), HttpStatusCode.BadRequest, "weak_password");

        // A refused password changes nothing, so the same link still works with a stronger one.
        Assert.Equal(HttpStatusCode.OK, (await ResetPasswordAsync(client, link, NewStrongPassword)).StatusCode);
    }

    [Fact]
    public async Task PasswordReset_Success_EndsExistingSessionsAndInvalidatesOlderLinks()
    {
        const string oldPassword = "the-original-password";
        var admin = await factory.SignInAsAsync("SuperAdmin", password: oldPassword);
        using var client = factory.CreateClient();

        var firstLink = await RequestPasswordResetAsync(client, admin.Email);
        var secondLink = await RequestPasswordResetAsync(client, admin.Email);

        // Requesting a new link withdrew the first one (FR-3).
        await AssertProblemAsync(
            await ResetPasswordAsync(client, firstLink, NewStrongPassword), HttpStatusCode.BadRequest, "password_reset_token_invalid");
        Assert.Equal(HttpStatusCode.OK, (await ResetPasswordAsync(client, secondLink, NewStrongPassword)).StatusCode);

        // The reset ended the session the old password started (FR-4) ...
        await AssertProblemAsync(
            await GetProfileAsync(client, admin.AccessToken), HttpStatusCode.Unauthorized, "session_revoked");
        using (var scope = factory.Services.CreateScope())
        {
            var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var session = await identityDb.Set<UserSession>().SingleAsync(s => s.Id == admin.SessionId);
            Assert.Equal(SessionRevocationReason.PasswordReset, session.RevokedReason);
        }

        // ... the used link cannot be replayed, and only the new password signs in, still
        // followed by the mandatory second factor.
        await AssertProblemAsync(
            await ResetPasswordAsync(client, secondLink, "yet another long passphrase"),
            HttpStatusCode.BadRequest,
            "password_reset_token_invalid");
        await AssertProblemAsync(
            await client.PostAsJsonAsync("/v1/auth/login", new { email = admin.Email, password = oldPassword }),
            HttpStatusCode.Unauthorized,
            "invalid_credentials");
        var loginResponse = await client.PostAsJsonAsync("/v1/auth/login", new { email = admin.Email, password = NewStrongPassword });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var pending = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(pending.GetProperty("requiresTwoFactor").GetBoolean());
    }

    [Fact]
    public async Task ParallelResets_WithOneLink_CannotBothConsumeIt()
    {
        var admin = await factory.SignInAsAsync("SuperAdmin", password: "the-original-password");
        using var client = factory.CreateClient();
        var link = await RequestPasswordResetAsync(client, admin.Email);

        // Two requests that loaded the same link before either saved, as two parallel
        // POST /v1/auth/password-reset/reset calls with it would.
        using var firstScope = factory.Services.CreateScope();
        using var secondScope = factory.Services.CreateScope();
        var first = await LoadResetTokenAsync(firstScope, link.TokenId);
        var second = await LoadResetTokenAsync(secondScope, link.TokenId);
        var nowUtc = firstScope.ServiceProvider.GetRequiredService<Clock>().UtcNow;

        Assert.True(first.IsUsable(nowUtc));
        first.Consume(nowUtc);
        await firstScope.ServiceProvider.GetRequiredService<IIdentityUnitOfWork>().SaveChangesAsync(CancellationToken.None);

        // The second copy is stale and still looks unused; the xmin row version is what stops
        // one link from setting two different passwords.
        Assert.True(second.IsUsable(nowUtc));
        second.Consume(nowUtc);
        var conflict = await Assert.ThrowsAsync<ConcurrencyConflictError>(
            () => secondScope.ServiceProvider.GetRequiredService<IIdentityUnitOfWork>().SaveChangesAsync(CancellationToken.None));
        Assert.IsType<DbUpdateConcurrencyException>(conflict.InnerException);
    }

    [Fact]
    public async Task ParallelResetRequests_ForAnExistingAccount_AllAnswerLikeAnUnknownEmail()
    {
        var admin = await factory.SignInAsAsync("SuperAdmin", password: "the-original-password");
        using var client = factory.CreateClient();
        var earlierLink = await RequestPasswordResetAsync(client, admin.Email);

        // Every one of these revokes the same earlier link. A lost race on that row must not
        // surface as a 409, which an unknown email can never get: that difference would tell
        // a caller the account exists (FR-1, NFR-5).
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            client.PostAsJsonAsync("/v1/auth/password-reset/request", new { email = admin.Email })));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        await AssertProblemAsync(
            await ResetPasswordAsync(client, earlierLink, NewStrongPassword), HttpStatusCode.BadRequest, "password_reset_token_invalid");
        Assert.Equal(
            HttpStatusCode.OK,
            (await ResetPasswordAsync(client, await RequestPasswordResetAsync(client, admin.Email), NewStrongPassword)).StatusCode);
    }

    [Fact]
    public async Task PasswordReset_ForAccountWithoutPassword_AnswersLikeAnUnknownEmailAndSendsNothing()
    {
        var candidate = await factory.SignInAsAsync("Candidate");
        var unknownEmail = $"nobody-{Guid.NewGuid():N}@tests.local";
        using var client = factory.CreateClient();

        var candidateResponse = await client.PostAsJsonAsync("/v1/auth/password-reset/request", new { email = candidate.Email });
        var unknownResponse = await client.PostAsJsonAsync("/v1/auth/password-reset/request", new { email = unknownEmail });

        // An OTP-only candidate (FR-1) must not gain a password login, and the answer must not
        // tell a caller that the account exists.
        Assert.Equal(HttpStatusCode.OK, candidateResponse.StatusCode);
        Assert.Equal(unknownResponse.StatusCode, candidateResponse.StatusCode);
        Assert.Equal(
            await unknownResponse.Content.ReadAsStringAsync(), await candidateResponse.Content.ReadAsStringAsync());
        Assert.False(factory.OtpSender.HasSentTo(candidate.Email));

        using var scope = factory.Services.CreateScope();
        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.False(await identityDb.PasswordResetTokens.AnyAsync(t => t.UserId == candidate.UserId));
    }

    // Arrange-only: no admin endpoint suspends accounts yet, so the domain method is called directly.
    private async Task SuspendAsync(Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var user = await services.GetRequiredService<IUserRepository>().GetByIdAsync(userId, CancellationToken.None)
            ?? throw new InvalidOperationException($"User {userId} was not found.");
        user.Suspend(services.GetRequiredService<Clock>().UtcNow);
        await services.GetRequiredService<IIdentityUnitOfWork>().SaveChangesAsync(CancellationToken.None);
    }

    // Arrange-only: starts a session that ended the given time ago (it began an hour before
    // that) and mints its token, since no real login can produce an already-expired session.
    private async Task<string> StartPastSessionAsync(Guid userId, TimeSpan sinceExpiry)
    {
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var user = await services.GetRequiredService<IUserRepository>().GetByIdAsync(userId, CancellationToken.None)
            ?? throw new InvalidOperationException($"User {userId} was not found.");
        var expiresAtUtc = services.GetRequiredService<Clock>().UtcNow - sinceExpiry;
        var session = user.StartNewSession(
            "past-session-hash", expiresAtUtc.AddHours(-1), expiresAtUtc, deviceFingerprint: null, ipAddress: null);
        await services.GetRequiredService<IIdentityUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return services.GetRequiredService<ITokenGenerator>().GenerateAccessToken(user, session);
    }

    // The token goes on this one request, so a test can try several tokens with one client.
    private static async Task<HttpResponseMessage> GetProfileAsync(HttpClient client, string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/me/profile");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await client.SendAsync(request);
    }

    private static async Task<PasswordResetToken> LoadResetTokenAsync(IServiceScope scope, Guid tokenId) =>
        await scope.ServiceProvider.GetRequiredService<IPasswordResetTokenRepository>().GetByIdAsync(tokenId, CancellationToken.None)
            ?? throw new InvalidOperationException($"Reset token {tokenId} was not found.");

    private static async Task<OtpChallenge> LoadChallengeAsync(IServiceScope scope, Guid challengeId) =>
        await scope.ServiceProvider.GetRequiredService<IOtpChallengeRepository>().GetByIdAsync(challengeId, CancellationToken.None)
            ?? throw new InvalidOperationException($"Challenge {challengeId} was not found.");

    private async Task AssertNoAccountAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.False(await identityDb.Users.AnyAsync(u => u.Email == email));
    }

    private static async Task<(string Email, Guid ChallengeId)> RegisterCandidateAsync(HttpClient client)
    {
        var email = $"security-{Guid.NewGuid():N}@tests.local";
        var response = await PostRegisterAsync(client, email, phoneNumber: null, otpChannel: "Email");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (email, body.GetProperty("otpChallengeId").GetGuid());
    }

    private static async Task<string?> GetDisplayNameAsync(HttpClient authenticatedClient)
    {
        var profile = await authenticatedClient.GetFromJsonAsync<JsonElement>("/v1/me/profile");
        return profile.GetProperty("displayName").GetString();
    }

    private static Task<HttpResponseMessage> PostRegisterAsync(
        HttpClient client, string? email, string? phoneNumber, string otpChannel, string? dateOfBirth = "1990-01-01") =>
        client.PostAsJsonAsync("/v1/auth/register", new
        {
            email,
            phoneNumber,
            dateOfBirth,
            displayName = "Security Candidate",
            otpChannel,
        });

    // Unique per call, so tests never collide on the unique phone index; 13 characters,
    // within the 20 an account stores.
    private static string UniquePhoneNumber() =>
        "+91" + Random.Shared.NextInt64(1_000_000_000, 10_000_000_000).ToString(CultureInfo.InvariantCulture);

    private static async Task<Guid> RequestOtpAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/v1/auth/otp/request", new { channel = "Email", destination = email });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Every request, for any destination or account state, must get this same shape,
        // or the response itself would tell a caller which accounts exist (FR-1, NFR-5).
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(["otpChallengeId"], body.EnumerateObject().Select(p => p.Name));
        return body.GetProperty("otpChallengeId").GetGuid();
    }

    // Requests a reset link and reads it back the way the email would carry it: "{tokenId}:{secret}".
    private async Task<(Guid TokenId, string Token)> RequestPasswordResetAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/v1/auth/password-reset/request", new { email });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var parts = factory.OtpSender.GetLastCode(email).Split(':');
        return (Guid.Parse(parts[0]), parts[1]);
    }

    private static Task<HttpResponseMessage> ResetPasswordAsync(
        HttpClient client, (Guid TokenId, string Token) link, string newPassword) =>
        client.PostAsJsonAsync(
            "/v1/auth/password-reset/reset",
            new { passwordResetTokenId = link.TokenId, token = link.Token, newPassword });

    private static Task<HttpResponseMessage> VerifyOtpAsync(HttpClient client, Guid challengeId, string code) =>
        client.PostAsJsonAsync("/v1/auth/otp/verify", new { otpChallengeId = challengeId, code });

    // A valid-looking 6-digit code guaranteed to differ from the real one.
    private static string DifferentCode(string code) =>
        ((int.Parse(code, CultureInfo.InvariantCulture) + 1) % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expectedStatus, string expectedErrorCode)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(expectedErrorCode, problem.GetProperty("title").GetString());
    }
}
