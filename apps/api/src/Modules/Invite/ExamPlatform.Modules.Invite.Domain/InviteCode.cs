namespace ExamPlatform.Modules.Invite.Domain;

/// <summary>A single-use invite code with an expiry and revocation tracking.</summary>
public class InviteCode
{
    public Guid Id { get; set; }
    public Guid InviteId { get; set; }

    /// <summary>The code, stored in upper case.</summary>
    public string Code { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Whether opening the link of this code signs the invited person in, as well as accepting the invitation. Only a code made for that
    /// purpose may do it: an ordinary code (typed in, or opened by someone who is already signed in) never signs anyone in.
    /// </summary>
    public bool AllowsDirectSignIn { get; set; }

    private InviteCode() { }

    public InviteCode(Guid inviteId, string code, int expiryHours, DateTime nowUtc, bool allowsDirectSignIn = false)
    {
        Id = Guid.NewGuid();
        InviteId = inviteId;
        Code = code;
        ExpiresAt = nowUtc.AddHours(expiryHours);
        CreatedAt = nowUtc;
        AllowsDirectSignIn = allowsDirectSignIn;
    }

    /// <summary>Whether the code can still be redeemed at <paramref name="nowUtc"/>: unused, not revoked, not past its expiry.</summary>
    /// <param name="nowUtc">The current instant.</param>
    public bool IsValid(DateTime nowUtc) => UsedAt is null && RevokedAt is null && nowUtc <= ExpiresAt;

    public void MarkAsUsed(DateTime nowUtc) => UsedAt = nowUtc;

    public void Revoke(DateTime nowUtc) => RevokedAt = nowUtc;
}
