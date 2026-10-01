using System.Globalization;
using System.Net;
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
/// the OTP brute-force lockout, replay protection, supersession of older codes and
/// concurrency guards (FR-1, NFR-5).
/// Each test arranges its own user with a unique address, so the tests are independent
/// of each other and of the order they run in.
/// </summary>
public sealed class IdentitySecurityFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
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
    public async Task GetOutstanding_ReturnsOnlyStillVerifiableChallengesForTheDestinationAndPurpose()
    {
        var destination = $"outstanding-{Guid.NewGuid():N}@tests.local";
        DateTime nowUtc;
        Guid liveChallengeId;

        using (var arrangeScope = factory.Services.CreateScope())
        {
            nowUtc = arrangeScope.ServiceProvider.GetRequiredService<Clock>().UtcNow;
            var validity = TimeSpan.FromMinutes(10);
            OtpChallenge Issue(string to, OtpPurpose purpose, DateTime issuedAt) =>
                OtpChallenge.Issue(null, OtpChannel.Email, to, "code-hash", purpose, issuedAt, validity);

            var live = Issue(destination, OtpPurpose.Login, nowUtc);
            var consumed = Issue(destination, OtpPurpose.Login, nowUtc);
            Assert.Equal(OtpVerificationOutcome.Verified, consumed.Verify("code-hash", nowUtc));
            var superseded = Issue(destination, OtpPurpose.Login, nowUtc);
            superseded.Supersede(nowUtc);
            var expired = Issue(destination, OtpPurpose.Login, nowUtc.AddMinutes(-20));
            var otherPurpose = Issue(destination, OtpPurpose.TwoFactorStep, nowUtc);
            var otherDestination = Issue($"other-{Guid.NewGuid():N}@tests.local", OtpPurpose.Login, nowUtc);

            var identityDb = arrangeScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            identityDb.OtpChallenges.AddRange(live, consumed, superseded, expired, otherPurpose, otherDestination);
            await identityDb.SaveChangesAsync();
            liveChallengeId = live.Id;
        }

        // A fresh scope, so the query reads the database rather than the change tracker.
        using var scope = factory.Services.CreateScope();
        var outstanding = await scope.ServiceProvider.GetRequiredService<IOtpChallengeRepository>()
            .GetOutstandingAsync(destination, OtpPurpose.Login, nowUtc, CancellationToken.None);

        Assert.Equal(liveChallengeId, Assert.Single(outstanding).Id);
    }

    private static async Task<OtpChallenge> LoadChallengeAsync(IServiceScope scope, Guid challengeId) =>
        await scope.ServiceProvider.GetRequiredService<IOtpChallengeRepository>().GetByIdAsync(challengeId, CancellationToken.None)
            ?? throw new InvalidOperationException($"Challenge {challengeId} was not found.");

    private static async Task<(string Email, Guid ChallengeId)> RegisterCandidateAsync(HttpClient client)
    {
        var email = $"security-{Guid.NewGuid():N}@tests.local";
        var response = await client.PostAsJsonAsync("/v1/auth/register", new
        {
            email,
            phoneNumber = (string?)null,
            dateOfBirth = "1990-01-01",
            displayName = "Security Candidate",
            otpChannel = "Email",
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (email, body.GetProperty("otpChallengeId").GetGuid());
    }

    private static async Task<Guid> RequestOtpAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/v1/auth/otp/request", new { channel = "Email", destination = email });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("otpChallengeId").GetGuid();
    }

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
