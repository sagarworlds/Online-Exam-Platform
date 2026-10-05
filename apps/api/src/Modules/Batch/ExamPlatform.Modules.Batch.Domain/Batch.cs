using ExamPlatform.SharedKernel.Domain;
using ExamPlatform.Modules.Batch.Domain.Events;
using ExamPlatform.Modules.Batch.Domain.Exceptions;

namespace ExamPlatform.Modules.Batch.Domain;

/// Batch aggregate root (FR-21). Manages exam batch membership, roster, and invitation lifecycle.
public class Batch : AggregateRoot
{
    public new Guid Id => base.Id;
    public Guid ExamId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public BatchStatus Status { get; private set; } = BatchStatus.Pending;
    public int MaxMembers { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public bool IsDeleted { get; private set; }

    private readonly List<BatchMember> _members = [];
    public IReadOnlyList<BatchMember> Members => _members.AsReadOnly();

    private Batch() : base(Guid.Empty) { }

    /// <summary>Creates a pending batch.</summary>
    /// <param name="examId">The exam the batch sits for.</param>
    /// <param name="name">Display name; must not be blank.</param>
    /// <param name="description">Optional longer description.</param>
    /// <param name="maxMembers">Capacity; must be greater than zero.</param>
    /// <param name="createdBy">The staff user creating the batch.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <exception cref="InvalidBatchConfigError">The name is blank or the capacity is not positive.</exception>
    public Batch(Guid examId, string name, string? description, int maxMembers, Guid createdBy, DateTime nowUtc)
        : base(Guid.NewGuid())
    {
        // The rules live on the aggregate, so no caller can build a batch that breaks them.
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidBatchConfigError("A batch needs a name.");
        if (maxMembers <= 0)
            throw new InvalidBatchConfigError("MaxMembers must be greater than zero.");

        ExamId = examId;
        Name = name.Trim();
        Description = description;
        MaxMembers = maxMembers;
        CreatedBy = createdBy;
        CreatedAt = nowUtc;
        UpdatedAt = nowUtc;

        AddDomainEvent(new BatchCreatedEvent(Id, ExamId, Name, CreatedBy));
    }

    /// <summary>Gives an e-mail address a seat in the batch.</summary>
    /// <param name="email">The member's address; stored trimmed and lower-cased.</param>
    /// <param name="phone">Optional phone number; stored without spaces or dashes.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <exception cref="InvalidBatchMemberError">The address or the phone number is not usable.</exception>
    /// <exception cref="DuplicateMemberError">The address already holds a seat.</exception>
    /// <exception cref="InvalidBatchConfigError">The batch is full.</exception>
    public void AddMember(string email, string? phone, DateTime nowUtc)
    {
        if (!MemberContact.TryNormalizeEmail(email, out var normalizedEmail))
            throw new InvalidBatchMemberError("A member needs a valid e-mail address.");

        string? normalizedPhone = null;
        if (!string.IsNullOrWhiteSpace(phone))
        {
            if (!MemberContact.TryNormalizePhone(phone, out var cleaned))
                throw new InvalidBatchMemberError("The phone number is not valid.");
            normalizedPhone = cleaned;
        }

        if (_members.Any(m => m.Email == normalizedEmail && !m.IsDeleted))
            throw new DuplicateMemberError();

        if (_members.Count(m => !m.IsDeleted) >= MaxMembers)
            throw new InvalidBatchConfigError("Batch has reached maximum member capacity.");

        _members.Add(new BatchMember(Id, normalizedEmail, normalizedPhone, nowUtc));
        UpdatedAt = nowUtc;
    }

    /// <summary>Takes a member out of the batch.</summary>
    /// <param name="memberId">The member to remove.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <exception cref="BatchMemberNotFoundError">The batch has no active member with that id.</exception>
    public void RemoveMember(Guid memberId, DateTime nowUtc)
    {
        var member = GetMember(memberId) ?? throw new BatchMemberNotFoundError(memberId);
        member.SoftDelete(nowUtc);
        UpdatedAt = nowUtc;
    }

    public BatchMember? GetMember(Guid memberId) =>
        _members.FirstOrDefault(m => m.Id == memberId && !m.IsDeleted);

    public BatchMember? GetMemberByEmail(string email) =>
        _members.FirstOrDefault(m => string.Equals(m.Email, email.Trim(), StringComparison.OrdinalIgnoreCase) && !m.IsDeleted);

    public int GetActiveMemberCount() => _members.Count(m => !m.IsDeleted);

    /// <summary>Opens the batch. It needs at least one member.</summary>
    /// <param name="nowUtc">The current instant.</param>
    /// <exception cref="InvalidBatchConfigError">The batch is not pending or has no members.</exception>
    public void Activate(DateTime nowUtc)
    {
        if (Status != BatchStatus.Pending)
            throw new InvalidBatchConfigError("Only pending batches can be activated.");

        if (!_members.Any(m => !m.IsDeleted))
            throw new InvalidBatchConfigError("Batch must have at least one member to activate.");

        Status = BatchStatus.Active;
        UpdatedAt = nowUtc;
        AddDomainEvent(new BatchActivatedEvent(Id, ExamId));
    }

    /// <summary>Closes an active batch.</summary>
    /// <param name="nowUtc">The current instant.</param>
    /// <exception cref="InvalidBatchConfigError">The batch is not active.</exception>
    public void Close(DateTime nowUtc)
    {
        if (Status != BatchStatus.Active)
            throw new InvalidBatchConfigError("Only active batches can be closed.");

        Status = BatchStatus.Closed;
        UpdatedAt = nowUtc;
        AddDomainEvent(new BatchClosedEvent(Id, ExamId));
    }

    public void SoftDelete(DateTime nowUtc)
    {
        IsDeleted = true;
        UpdatedAt = nowUtc;
    }
}
