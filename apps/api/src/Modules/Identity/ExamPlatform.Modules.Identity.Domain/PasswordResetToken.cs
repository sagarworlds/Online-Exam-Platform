using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Identity.Domain;

/// <summary>A single-use token authorizing one password reset for a user.</summary>
public sealed class PasswordResetToken : AggregateRoot
{
    /// <summary>The user this token allows a password reset for.</summary>
    public Guid UserId { get; private set; }

    /// <summary>Hash of the token, never the raw value.</summary>
    public string TokenHash { get; private set; }

    /// <summary>When the token stops being acceptable.</summary>
    public DateTime ExpiresAtUtc { get; private set; }

    /// <summary>When the token was used, if it has been.</summary>
    public DateTime? ConsumedAtUtc { get; private set; }

    /// <summary>
    /// When the token was withdrawn without being used, if it has been: a newer reset link
    /// was requested, or the password was reset through another link (FR-3).
    /// </summary>
    public DateTime? RevokedAtUtc { get; private set; }

    private PasswordResetToken(Guid id, Guid userId, string tokenHash, DateTime expiresAtUtc) : base(id)
    {
        UserId = userId;
        TokenHash = tokenHash;
        ExpiresAtUtc = expiresAtUtc;
    }

    /// <summary>Issues a new password reset token.</summary>
    /// <param name="userId">The user the token is for.</param>
    /// <param name="tokenHash">Hash of the generated token.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <param name="validity">How long the token remains acceptable.</param>
    public static PasswordResetToken Issue(Guid userId, string tokenHash, DateTime nowUtc, TimeSpan validity) =>
        new(Guid.NewGuid(), userId, tokenHash, nowUtc.Add(validity));

    /// <summary>Whether the token can still be used: not consumed, not revoked, and not past its expiry.</summary>
    /// <param name="nowUtc">The instant to evaluate against.</param>
    public bool IsUsable(DateTime nowUtc) => ConsumedAtUtc is null && RevokedAtUtc is null && nowUtc <= ExpiresAtUtc;

    /// <summary>Marks the token used, so it cannot be replayed.</summary>
    /// <param name="nowUtc">The current instant.</param>
    public void Consume(DateTime nowUtc) => ConsumedAtUtc = nowUtc;

    /// <summary>
    /// Withdraws the token so its link stops working. A no-op for a token that is already
    /// consumed (its outcome is final) or already revoked (the first revocation time is kept).
    /// </summary>
    /// <param name="nowUtc">The current instant.</param>
    public void Revoke(DateTime nowUtc)
    {
        if (ConsumedAtUtc is not null || RevokedAtUtc is not null)
        {
            return;
        }

        RevokedAtUtc = nowUtc;
    }
}
