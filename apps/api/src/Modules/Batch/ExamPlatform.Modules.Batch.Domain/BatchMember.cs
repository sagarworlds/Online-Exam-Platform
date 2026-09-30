namespace ExamPlatform.Modules.Batch.Domain;

public class BatchMember
{
    public Guid Id { get; set; }
    public Guid BatchId { get; set; }
    public Guid? CandidateId { get; set; }
    public string Email { get; set; } = null!;
    public string? Phone { get; set; }
    public MemberRegistrationStatus Status { get; set; } = MemberRegistrationStatus.Invited;
    public DateTime? InviteSentAt { get; set; }
    public DateTime? RegistrationCompletedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }

    private BatchMember() { }

    public BatchMember(Guid batchId, string email, string? phone = null)
    {
        Id = Guid.NewGuid();
        BatchId = batchId;
        Email = email;
        Phone = phone;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkInviteSent()
    {
        InviteSentAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkRegistrationCompleted(Guid candidateId)
    {
        CandidateId = candidateId;
        Status = MemberRegistrationStatus.Registered;
        RegistrationCompletedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Withdraw()
    {
        Status = MemberRegistrationStatus.Withdrawn;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SoftDelete()
    {
        IsDeleted = true;
        UpdatedAt = DateTime.UtcNow;
    }
}
