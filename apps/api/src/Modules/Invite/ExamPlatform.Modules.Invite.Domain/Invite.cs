using ExamPlatform.SharedKernel.Domain;
using ExamPlatform.Modules.Invite.Domain.Events;
using ExamPlatform.Modules.Invite.Domain.Exceptions;

namespace ExamPlatform.Modules.Invite.Domain;

/// Invite aggregate root (FR-23, FR-24, FR-25). Manages exam invitations and single-use invite codes.
public class Invite : AggregateRoot
{
    public new Guid Id => base.Id;
    public Guid ExamId { get; set; }
    public Guid BatchMemberId { get; set; }
    public string Email { get; set; } = null!;
    public InviteStatus Status { get; set; } = InviteStatus.Pending;
    public DateTime SentAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public DateTime? DeclinedAt { get; set; }
    public DateTime CreatedBy { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }

    private readonly List<InviteCode> _codes = [];
    public IReadOnlyList<InviteCode> Codes => _codes.AsReadOnly();

    private Invite() : base(Guid.Empty) { }

    public Invite(Guid examId, Guid batchMemberId, string email, Guid createdByUserId)
        : base(Guid.NewGuid())
    {
        ExamId = examId;
        BatchMemberId = batchMemberId;
        Email = email;
        CreatedByUserId = createdByUserId;
        SentAt = DateTime.UtcNow;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;

        AddDomainEvent(new InviteCreatedEvent(Id, ExamId, Email, CreatedByUserId));
    }

    public InviteCode GenerateCode(int expiryHours = 72)
    {
        var code = new InviteCode(Id, GenerateUniqueCode(), expiryHours);
        _codes.Add(code);
        UpdatedAt = DateTime.UtcNow;
        return code;
    }

    public InviteCode? GetValidCode()
    {
        return _codes.FirstOrDefault(c => c.IsValid());
    }

    public void RevokeAllCodes()
    {
        foreach (var code in _codes.Where(c => !c.RevokedAt.HasValue && !c.UsedAt.HasValue))
        {
            code.Revoke();
        }
        UpdatedAt = DateTime.UtcNow;
    }

    public void Accept(Guid inviteCodeId)
    {
        if (Status != InviteStatus.Pending)
            throw new InvalidOperationException("Only pending invites can be accepted.");

        var code = _codes.FirstOrDefault(c => c.Id == inviteCodeId);
        if (code == null || !code.IsValid())
            throw new InvalidInviteCodeError("Code not found or invalid.");

        code.MarkAsUsed();
        Status = InviteStatus.Accepted;
        AcceptedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;

        AddDomainEvent(new InviteAcceptedEvent(Id, ExamId, Email));
    }

    public void Decline()
    {
        if (Status != InviteStatus.Pending)
            throw new InvalidOperationException("Only pending invites can be declined.");

        Status = InviteStatus.Declined;
        DeclinedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;

        AddDomainEvent(new InviteDeclinedEvent(Id, ExamId, Email));
    }

    public void Revoke()
    {
        if (Status is InviteStatus.Revoked or InviteStatus.Expired)
            throw new InvalidOperationException("Cannot revoke an already revoked or expired invite.");

        RevokeAllCodes();
        Status = InviteStatus.Revoked;
        UpdatedAt = DateTime.UtcNow;

        AddDomainEvent(new InviteRevokedEvent(Id, ExamId, Email));
    }

    public void SoftDelete()
    {
        IsDeleted = true;
        UpdatedAt = DateTime.UtcNow;
    }

    private static string GenerateUniqueCode()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var random = new Random();
        var code = new string(Enumerable.Range(0, 8)
            .Select(_ => chars[random.Next(chars.Length)])
            .ToArray());
        return code;
    }
}
