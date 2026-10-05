namespace ExamPlatform.Modules.Batch.Domain;

public class BatchMember
{
    public Guid Id { get; private set; }
    public Guid BatchId { get; private set; }
    public Guid? CandidateId { get; private set; }
    public string Email { get; private set; } = null!;
    public string? Phone { get; private set; }
    public MemberRegistrationStatus Status { get; private set; } = MemberRegistrationStatus.Invited;
    public DateTime? InviteSentAt { get; private set; }
    public DateTime? RegistrationCompletedAt { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public bool IsDeleted { get; private set; }

    private BatchMember() { }

    internal BatchMember(Guid batchId, string email, string? phone, DateTime nowUtc)
    {
        Id = Guid.NewGuid();
        BatchId = batchId;
        Email = email;
        Phone = phone;
        CreatedAt = nowUtc;
        UpdatedAt = nowUtc;
    }

    public void MarkInviteSent(DateTime nowUtc)
    {
        InviteSentAt = nowUtc;
        UpdatedAt = nowUtc;
    }

    public void MarkRegistrationCompleted(Guid candidateId, DateTime nowUtc)
    {
        CandidateId = candidateId;
        Status = MemberRegistrationStatus.Registered;
        RegistrationCompletedAt = nowUtc;
        UpdatedAt = nowUtc;
    }

    public void Withdraw(DateTime nowUtc)
    {
        Status = MemberRegistrationStatus.Withdrawn;
        UpdatedAt = nowUtc;
    }

    public void SoftDelete(DateTime nowUtc)
    {
        IsDeleted = true;
        UpdatedAt = nowUtc;
    }
}
