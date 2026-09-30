namespace ExamPlatform.Modules.Guardian.Domain;

/// Represents the verified link between a guardian and a minor candidate.
public class GuardianLink
{
    public Guid Id { get; set; }
    public Guid GuardianId { get; set; }
    public Guid CandidateId { get; set; }
    public string CandidateEmail { get; set; } = null!;
    public string VerificationToken { get; set; } = null!;
    public GuardianLinkStatus Status { get; set; } = GuardianLinkStatus.Pending;
    public DateTime? VerifiedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }

    private GuardianLink() { }

    public GuardianLink(Guid guardianId, Guid candidateId, string candidateEmail, string verificationToken)
    {
        Id = Guid.NewGuid();
        GuardianId = guardianId;
        CandidateId = candidateId;
        CandidateEmail = candidateEmail;
        VerificationToken = verificationToken;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Verify()
    {
        if (Status != GuardianLinkStatus.Pending)
            throw new InvalidOperationException("Only pending links can be verified.");

        Status = GuardianLinkStatus.Verified;
        VerifiedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Revoke()
    {
        if (Status == GuardianLinkStatus.Revoked)
            throw new InvalidOperationException("Link is already revoked.");

        Status = GuardianLinkStatus.Revoked;
        RevokedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public bool IsVerified() => Status == GuardianLinkStatus.Verified && !IsDeleted;

    public void SoftDelete()
    {
        IsDeleted = true;
        UpdatedAt = DateTime.UtcNow;
    }
}
