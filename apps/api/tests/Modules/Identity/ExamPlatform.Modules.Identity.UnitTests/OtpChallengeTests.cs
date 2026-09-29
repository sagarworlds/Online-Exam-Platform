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
    public void Verify_WithExpiredChallenge_ThrowsOtpExpiredError()
    {
        var challenge = CreateChallenge();
        var afterExpiry = IssuedAt.AddMinutes(11);

        var act = () => challenge.Verify("correct-hash", afterExpiry);

        Assert.Throws<OtpExpiredError>(act);
    }

    [Fact]
    public void Verify_WithCorrectCodeBeforeExpiry_Succeeds()
    {
        var challenge = CreateChallenge();

        challenge.Verify("correct-hash", IssuedAt.AddMinutes(5));

        Assert.True(challenge.IsConsumed);
    }

    [Fact]
    public void Verify_WithWrongCode_ThrowsOtpMismatchError()
    {
        var challenge = CreateChallenge();

        var act = () => challenge.Verify("wrong-hash", IssuedAt.AddMinutes(1));

        Assert.Throws<OtpMismatchError>(act);
    }

    [Fact]
    public void Verify_WithWrongCodeRepeatedly_ThrowsOtpAttemptsExceededError()
    {
        var challenge = CreateChallenge(maxAttempts: 3);

        for (var i = 0; i < 3; i++)
        {
            Assert.Throws<OtpMismatchError>(() => challenge.Verify("wrong-hash", IssuedAt.AddMinutes(1)));
        }

        // The 4th attempt has already exhausted MaxAttempts, regardless of correctness.
        var act = () => challenge.Verify("correct-hash", IssuedAt.AddMinutes(1));

        Assert.Throws<OtpAttemptsExceededError>(act);
    }
}
