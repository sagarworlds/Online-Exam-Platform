using ExamPlatform.SharedKernel.Domain;
using ExamPlatform.Modules.Batch.Domain.Events;
using ExamPlatform.Modules.Batch.Domain.Exceptions;

namespace ExamPlatform.Modules.Batch.Domain;

/// Batch aggregate root (FR-21). Manages exam batch membership, roster, and invitation lifecycle.
public class Batch : AggregateRoot
{
    public new Guid Id => base.Id;
    public Guid ExamId { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public BatchStatus Status { get; set; } = BatchStatus.Pending;
    public int MaxMembers { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }

    private readonly List<BatchMember> _members = [];
    public IReadOnlyList<BatchMember> Members => _members.AsReadOnly();

    private Batch() : base(Guid.Empty) { }

    public Batch(Guid examId, string name, string? description, int maxMembers, Guid createdBy)
        : base(Guid.NewGuid())
    {
        ExamId = examId;
        Name = name;
        Description = description;
        MaxMembers = maxMembers;
        CreatedBy = createdBy;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;

        AddDomainEvent(new BatchCreatedEvent(Id, ExamId, Name, CreatedBy));
    }

    public void AddMember(string email, string? phone = null)
    {
        if (_members.Any(m => m.Email == email && !m.IsDeleted))
            throw new DuplicateMemberError(email);

        if (_members.Count(m => !m.IsDeleted) >= MaxMembers)
            throw new InvalidBatchConfigError("Batch has reached maximum member capacity.");

        var member = new BatchMember(Id, email, phone);
        _members.Add(member);
        UpdatedAt = DateTime.UtcNow;
    }

    public void RemoveMember(Guid memberId)
    {
        var member = _members.FirstOrDefault(m => m.Id == memberId && !m.IsDeleted);
        if (member != null)
        {
            member.SoftDelete();
            UpdatedAt = DateTime.UtcNow;
        }
    }

    public BatchMember? GetMember(Guid memberId) =>
        _members.FirstOrDefault(m => m.Id == memberId && !m.IsDeleted);

    public BatchMember? GetMemberByEmail(string email) =>
        _members.FirstOrDefault(m => m.Email == email && !m.IsDeleted);

    public int GetActiveMemberCount() => _members.Count(m => !m.IsDeleted);

    public void Activate()
    {
        if (Status != BatchStatus.Pending)
            throw new InvalidBatchConfigError("Only pending batches can be activated.");

        if (!_members.Any(m => !m.IsDeleted))
            throw new InvalidBatchConfigError("Batch must have at least one member to activate.");

        Status = BatchStatus.Active;
        UpdatedAt = DateTime.UtcNow;
        AddDomainEvent(new BatchActivatedEvent(Id, ExamId));
    }

    public void Close()
    {
        if (Status != BatchStatus.Active)
            throw new InvalidBatchConfigError("Only active batches can be closed.");

        Status = BatchStatus.Closed;
        UpdatedAt = DateTime.UtcNow;
        AddDomainEvent(new BatchClosedEvent(Id, ExamId));
    }

    public void SoftDelete()
    {
        IsDeleted = true;
        UpdatedAt = DateTime.UtcNow;
    }
}
