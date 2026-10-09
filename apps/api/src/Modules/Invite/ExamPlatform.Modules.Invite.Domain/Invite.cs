using System.Security.Cryptography;
using ExamPlatform.SharedKernel.Domain;
using ExamPlatform.Modules.Invite.Domain.Events;
using ExamPlatform.Modules.Invite.Domain.Exceptions;

namespace ExamPlatform.Modules.Invite.Domain;

/// <summary>
/// Invite aggregate root (FR-14, FR-50a): an invitation of one e-mail address to one exam, redeemed with a
/// single-use code. Whoever accepts must hold the invited address, so an invite cannot be passed to someone else.
/// </summary>
public class Invite : AggregateRoot
{
    /// <summary>The fewest hours a code may live.</summary>
    public const int MinCodeExpiryHours = 1;

    /// <summary>The most hours a code may live (30 days).</summary>
    public const int MaxCodeExpiryHours = 720;

    private const string CodeAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
    private const int CodeLength = 8;

    public new Guid Id => base.Id;
    public Guid ExamId { get; set; }

    /// <summary>The roster entry this invite is for, when it came from a batch; null for a direct invite.</summary>
    public Guid? BatchMemberId { get; set; }
    public string Email { get; set; } = null!;
    public InviteStatus Status { get; set; } = InviteStatus.Pending;
    public DateTime SentAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public DateTime? DeclinedAt { get; set; }
    public Guid CreatedByUserId { get; set; }

    /// <summary>The account that redeemed the invite; set when it is accepted.</summary>
    public Guid? AcceptedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }

    private readonly List<InviteCode> _codes = [];
    public IReadOnlyList<InviteCode> Codes => _codes.AsReadOnly();

    private Invite() : base(Guid.Empty) { }

    /// <summary>Creates a pending invite.</summary>
    /// <param name="examId">The exam the address is invited to.</param>
    /// <param name="batchMemberId">The roster entry it came from, if any.</param>
    /// <param name="email">The invited e-mail address.</param>
    /// <param name="createdByUserId">The staff user who invited.</param>
    /// <param name="nowUtc">The current instant.</param>
    public Invite(Guid examId, Guid? batchMemberId, string email, Guid createdByUserId, DateTime nowUtc)
        : base(Guid.NewGuid())
    {
        ExamId = examId;
        BatchMemberId = batchMemberId;
        Email = email;
        CreatedByUserId = createdByUserId;
        SentAt = nowUtc;
        CreatedAt = nowUtc;
        UpdatedAt = nowUtc;

        AddDomainEvent(new InviteCreatedEvent(Id, ExamId, Email, CreatedByUserId));
    }

    /// <summary>Adds a new single-use code to the invite.</summary>
    /// <param name="expiryHours">How long the code lives, from <see cref="MinCodeExpiryHours"/> to <see cref="MaxCodeExpiryHours"/>.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <exception cref="InvalidInviteExpiryError">The lifetime is outside the allowed range.</exception>
    public InviteCode GenerateCode(int expiryHours, DateTime nowUtc)
    {
        EnsureCodeLifetime(expiryHours);

        // A cryptographic generator: the code is the credential that lets someone take the exam.
        var code = new InviteCode(Id, RandomNumberGenerator.GetString(CodeAlphabet, CodeLength), expiryHours, nowUtc);
        _codes.Add(code);
        UpdatedAt = nowUtc;
        return code;
    }

    /// <summary>
    /// Adds another single-use code to a pending invite, for staff to hand to the invited person themselves (read out, pasted into a chat,
    /// printed), and records that they did. The invite's first code, made with it, is not announced this way.
    /// </summary>
    /// <param name="expiryHours">How long the code lives, from <see cref="MinCodeExpiryHours"/> to <see cref="MaxCodeExpiryHours"/>.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <exception cref="InvalidInviteExpiryError">The lifetime is outside the allowed range.</exception>
    /// <exception cref="InviteStateError">
    /// The invite is no longer pending: a code for an accepted, declined, revoked or expired invite could never be redeemed, so none is made.
    /// </exception>
    public InviteCode AddCode(int expiryHours, DateTime nowUtc)
    {
        // The lifetime first: a bad one is refused the same way whatever state the invite is in.
        EnsureCodeLifetime(expiryHours);

        if (Status != InviteStatus.Pending)
            throw new InviteStateError("Only a pending invitation can be given another code.");

        var code = GenerateCode(expiryHours, nowUtc);
        AddDomainEvent(new InviteCodeGeneratedEvent(Id, ExamId, code.Id));
        return code;
    }

    private static void EnsureCodeLifetime(int expiryHours)
    {
        if (expiryHours is < MinCodeExpiryHours or > MaxCodeExpiryHours)
            throw new InvalidInviteExpiryError(MinCodeExpiryHours, MaxCodeExpiryHours);
    }

    /// <summary>
    /// Redeems a code on behalf of a signed-in user, who must hold the invited address.
    /// </summary>
    /// <param name="code">The code as typed or copied from the link; case is ignored.</param>
    /// <param name="acceptedByUserId">The account accepting.</param>
    /// <param name="acceptedByEmail">The accepting account's verified e-mail address, if it has one.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <exception cref="InviteStateError">The invite is no longer pending.</exception>
    /// <exception cref="InvalidInviteCodeError">The code is not one of this invite's, or is used, revoked or expired.</exception>
    /// <exception cref="InviteEmailMismatchError">The accepting account's address is not the invited one.</exception>
    public void Accept(string? code, Guid acceptedByUserId, string? acceptedByEmail, DateTime nowUtc)
    {
        // The code is looked up before anything about the invite is revealed: a wrong code, an
        // already-used one and an expired one all look the same to a caller who guesses.
        var normalized = code?.Trim().ToUpperInvariant();
        var match = _codes.FirstOrDefault(c => c.Code == normalized);
        if (match is null || !match.IsValid(nowUtc))
            throw new InvalidInviteCodeError("the code is not valid");

        if (Status != InviteStatus.Pending)
            throw new InviteStateError("This invitation is no longer open.");

        if (!string.Equals(Email.Trim(), acceptedByEmail?.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new InviteEmailMismatchError();

        match.MarkAsUsed(nowUtc);
        Status = InviteStatus.Accepted;
        AcceptedAt = nowUtc;
        AcceptedByUserId = acceptedByUserId;
        UpdatedAt = nowUtc;

        AddDomainEvent(new InviteAcceptedEvent(Id, ExamId, Email));
    }

    /// <summary>Declines a pending invite.</summary>
    /// <param name="nowUtc">The current instant.</param>
    /// <exception cref="InviteStateError">The invite is no longer pending.</exception>
    public void Decline(DateTime nowUtc)
    {
        if (Status != InviteStatus.Pending)
            throw new InviteStateError("Only a pending invitation can be declined.");

        Status = InviteStatus.Declined;
        DeclinedAt = nowUtc;
        UpdatedAt = nowUtc;

        AddDomainEvent(new InviteDeclinedEvent(Id, ExamId, Email));
    }

    /// <summary>Revokes the invite and every code that is still unused.</summary>
    /// <param name="nowUtc">The current instant.</param>
    /// <exception cref="InviteStateError">The invite is already revoked or expired.</exception>
    public void Revoke(DateTime nowUtc)
    {
        if (Status is InviteStatus.Revoked or InviteStatus.Expired)
            throw new InviteStateError("This invitation is already revoked or expired.");

        foreach (var code in _codes.Where(c => c.RevokedAt is null && c.UsedAt is null))
            code.Revoke(nowUtc);

        Status = InviteStatus.Revoked;
        UpdatedAt = nowUtc;

        AddDomainEvent(new InviteRevokedEvent(Id, ExamId, Email));
    }

    public void SoftDelete(DateTime nowUtc)
    {
        IsDeleted = true;
        UpdatedAt = nowUtc;
    }
}
