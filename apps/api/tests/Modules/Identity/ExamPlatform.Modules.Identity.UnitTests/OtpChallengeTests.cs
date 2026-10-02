using ExamPlatform.Modules.Identity.Domain;
using ExamPlatform.Modules.Identity.Domain.Exceptions;

namespace ExamPlatform.Modules.Identity.UnitTests;

public class OtpChallengeTests
{
    private static readonly DateTime IssuedAt = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static OtpChallenge CreateChallenge(string codeHash = "correct-hash", int maxAttempts = 5) =>
        OtpChallenge.Issue(
            userId: Guid.NewGuid(),
            channel: OtpChannel.Email,
            destination: "candidate@example.com",
            codeHash: codeHash,
            purpose: OtpPurpose.Login,
            nowUtc: IssuedAt,
            validity: TimeSpan.FromMinutes(10),
            maxAttempts: maxAttempts);

    [Fact]
    public void Verify_AfterExpiry_ReturnsExpired()
    {
        var challenge = CreateChallenge();
        var afterExpiry = IssuedAt.AddMinutes(11);

        var outcome = challenge.Verify("correct-hash", afterExpiry);

        Assert.Equal(OtpVerificationOutcome.Expired, outcome);
        Assert.False(challenge.IsConsumed);
        Assert.Equal(0, challenge.AttemptCount);
    }

    [Fact]
    public void Verify_AtExactExpiryInstant_IsStillAccepted()
    {
        var challenge = CreateChallenge();

        // Expiry is exclusive of the instant itself; GetOutstandingAsync relies on the same
        // boundary, so a challenge still verifiable at its expiry instant is also superseded.
        var outcome = challenge.Verify("correct-hash", challenge.ExpiresAtUtc);

        Assert.Equal(OtpVerificationOutcome.Verified, outcome);
    }

    [Fact]
    public void Verify_WithCorrectCode_ReturnsVerifiedAndConsumes()
    {
        var challenge = CreateChallenge();
        var verifiedAt = IssuedAt.AddMinutes(5);

        var outcome = challenge.Verify("correct-hash", verifiedAt);

        Assert.Equal(OtpVerificationOutcome.Verified, outcome);
        Assert.True(challenge.IsConsumed);
        Assert.Equal(verifiedAt, challenge.ConsumedAtUtc);
    }

    [Fact]
    public void Verify_WithWrongCode_ReturnsMismatchAndIncrementsAttemptCount()
    {
        var challenge = CreateChallenge();

        var outcome = challenge.Verify("wrong-hash", IssuedAt.AddMinutes(1));

        Assert.Equal(OtpVerificationOutcome.Mismatch, outcome);
        Assert.Equal(1, challenge.AttemptCount);
        Assert.False(challenge.IsConsumed);
    }

    [Fact]
    public void Verify_AfterMaxWrongAttempts_ReturnsAttemptsExceededEvenForCorrectCode()
    {
        var challenge = CreateChallenge(maxAttempts: 3);

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(OtpVerificationOutcome.Mismatch, challenge.Verify("wrong-hash", IssuedAt.AddMinutes(1)));
        }

        // The 4th attempt has already exhausted MaxAttempts, regardless of correctness.
        var outcome = challenge.Verify("correct-hash", IssuedAt.AddMinutes(1));

        Assert.Equal(OtpVerificationOutcome.AttemptsExceeded, outcome);
        Assert.Equal(3, challenge.AttemptCount);
        Assert.False(challenge.IsConsumed);
    }

    [Fact]
    public void Verify_WhenAlreadyConsumed_ReturnsAlreadyUsed()
    {
        var challenge = CreateChallenge();
        var firstVerifiedAt = IssuedAt.AddMinutes(1);
        Assert.Equal(OtpVerificationOutcome.Verified, challenge.Verify("correct-hash", firstVerifiedAt));

        // Replaying the same, correct code must not succeed a second time (each success mints a session).
        var outcome = challenge.Verify("correct-hash", IssuedAt.AddMinutes(2));

        Assert.Equal(OtpVerificationOutcome.AlreadyUsed, outcome);
        Assert.Equal(firstVerifiedAt, challenge.ConsumedAtUtc);
        Assert.Equal(1, challenge.AttemptCount);
    }

    [Fact]
    public void Verify_AfterSupersede_ReturnsSuperseded()
    {
        var challenge = CreateChallenge();
        var supersededAt = IssuedAt.AddMinutes(1);
        challenge.Supersede(supersededAt);

        // Even the right code is refused once a newer one exists, and the refusal costs no
        // attempt: the attempt budget belongs to the newest code alone.
        var outcome = challenge.Verify("correct-hash", IssuedAt.AddMinutes(2));

        Assert.Equal(OtpVerificationOutcome.Superseded, outcome);
        Assert.True(challenge.IsSuperseded);
        Assert.Equal(supersededAt, challenge.SupersededAtUtc);
        Assert.False(challenge.IsConsumed);
        Assert.Equal(0, challenge.AttemptCount);
    }

    [Fact]
    public void Supersede_WhenAlreadyConsumed_IsNoOp()
    {
        var challenge = CreateChallenge();
        Assert.Equal(OtpVerificationOutcome.Verified, challenge.Verify("correct-hash", IssuedAt.AddMinutes(1)));

        challenge.Supersede(IssuedAt.AddMinutes(2));

        // A consumed challenge's outcome is final, so a replay still reports AlreadyUsed.
        Assert.False(challenge.IsSuperseded);
        Assert.Null(challenge.SupersededAtUtc);
        Assert.Equal(OtpVerificationOutcome.AlreadyUsed, challenge.Verify("correct-hash", IssuedAt.AddMinutes(3)));
    }

    [Fact]
    public void Supersede_WhenAlreadySuperseded_KeepsTheFirstTime()
    {
        var challenge = CreateChallenge();
        var firstSupersededAt = IssuedAt.AddMinutes(1);
        challenge.Supersede(firstSupersededAt);

        challenge.Supersede(IssuedAt.AddMinutes(2));

        Assert.Equal(firstSupersededAt, challenge.SupersededAtUtc);
    }

    [Theory]
    [InlineData(OtpVerificationOutcome.Mismatch, typeof(OtpMismatchError), "otp_mismatch")]
    [InlineData(OtpVerificationOutcome.Expired, typeof(OtpExpiredError), "otp_expired")]
    [InlineData(OtpVerificationOutcome.AttemptsExceeded, typeof(OtpAttemptsExceededError), "otp_attempts_exceeded")]
    [InlineData(OtpVerificationOutcome.AlreadyUsed, typeof(OtpAlreadyUsedError), "otp_already_used")]
    [InlineData(OtpVerificationOutcome.Superseded, typeof(OtpSupersededError), "otp_superseded")]
    public void ToError_MapsEachFailureToItsTypedError(OtpVerificationOutcome outcome, Type expectedErrorType, string expectedErrorCode)
    {
        var error = outcome.ToError();

        Assert.IsType(expectedErrorType, error);
        Assert.Equal(expectedErrorCode, error.ErrorCode);
    }

    [Fact]
    public void ToError_ForVerified_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => OtpVerificationOutcome.Verified.ToError());
    }
}
