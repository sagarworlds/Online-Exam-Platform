namespace ExamPlatform.Modules.Invite.Domain;

/// Single-use invite code with expiry and revocation tracking.
public class InviteCode
{
    public Guid Id { get; set; }
    public Guid InviteId { get; set; }
    public string Code { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    private InviteCode() { }

    public InviteCode(Guid inviteId, string code, int expiryHours = 72)
    {
        Id = Guid.NewGuid();
        InviteId = inviteId;
        Code = code;
        ExpiresAt = DateTime.UtcNow.AddHours(expiryHours);
        CreatedAt = DateTime.UtcNow;
    }

    public bool IsValid()
    {
        if (UsedAt.HasValue)
            return false;

        if (RevokedAt.HasValue)
            return false;

        return DateTime.UtcNow <= ExpiresAt;
    }

    public bool IsExpired() => DateTime.UtcNow > ExpiresAt;

    public void MarkAsUsed()
    {
        UsedAt = DateTime.UtcNow;
    }

    public void Revoke()
    {
        RevokedAt = DateTime.UtcNow;
    }
}
