using ExamPlatform.Modules.Identity.Domain;

namespace ExamPlatform.Modules.Identity.UnitTests;

public class PasswordResetTokenTests
{
    private static readonly DateTime IssuedAt = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static PasswordResetToken CreateToken() =>
        PasswordResetToken.Issue(Guid.NewGuid(), "token-hash", IssuedAt, TimeSpan.FromMinutes(30));

    [Fact]
    public void Revoked_IsNotUsable()
    {
        var token = CreateToken();

        token.Revoke(IssuedAt.AddMinutes(1));

        Assert.False(token.IsUsable(IssuedAt.AddMinutes(2)));
        Assert.Equal(IssuedAt.AddMinutes(1), token.RevokedAtUtc);
    }

    [Fact]
    public void IsUsable_AtExactExpiryInstant_IsTrue()
    {
        var token = CreateToken();

        // GetOutstandingForUserAsync relies on the same inclusive boundary, so a token still
        // usable at its expiry instant is also revoked by a newer request or a reset.
        Assert.True(token.IsUsable(IssuedAt.AddMinutes(30)));
        Assert.False(token.IsUsable(IssuedAt.AddMinutes(30).AddTicks(1)));
    }

    [Fact]
    public void Revoke_WhenConsumed_LeavesItConsumedOnly()
    {
        var token = CreateToken();
        token.Consume(IssuedAt.AddMinutes(1));

        token.Revoke(IssuedAt.AddMinutes(2));

        Assert.Null(token.RevokedAtUtc);
        Assert.Equal(IssuedAt.AddMinutes(1), token.ConsumedAtUtc);
    }

    [Fact]
    public void Revoke_WhenAlreadyRevoked_KeepsTheFirstTime()
    {
        var token = CreateToken();
        token.Revoke(IssuedAt.AddMinutes(1));

        token.Revoke(IssuedAt.AddMinutes(2));

        Assert.Equal(IssuedAt.AddMinutes(1), token.RevokedAtUtc);
    }
}
