using ExamPlatform.Modules.Guardian.Domain.Exceptions;

namespace ExamPlatform.Modules.Guardian.Domain;

/// Represents the link between a guardian and a minor candidate, from the guardian's request until they confirm it.
public class GuardianLink
{
    public Guid Id { get; set; }
    public Guid GuardianId { get; set; }
    public Guid CandidateId { get; set; }
    public string CandidateEmail { get; set; } = null!;

    /// The SHA-256 of the one-time code e-mailed to the guardian. The code itself is never stored, so a copy of the
    /// database cannot be used to confirm a link (NFR-5).
    public string VerificationTokenHash { get; set; } = null!;

    /// When the e-mailed code stops working; a guardian who waits longer must have the link requested again.
    public DateTime VerificationExpiresAt { get; set; }

    public GuardianLinkStatus Status { get; set; } = GuardianLinkStatus.Pending;
    public DateTime? VerifiedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }

    private GuardianLink() { }

    public GuardianLink(Guid guardianId, Guid candidateId, string candidateEmail, string verificationTokenHash, DateTime verificationExpiresAt)
    {
        Id = Guid.NewGuid();
        GuardianId = guardianId;
        CandidateId = candidateId;
        CandidateEmail = candidateEmail;
        VerificationTokenHash = verificationTokenHash;
        VerificationExpiresAt = verificationExpiresAt;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Confirms the link with the guardian's code. A link is confirmed once; a second use is refused.</summary>
    /// <param name="nowUtc">The moment of confirmation, checked against when the code expires.</param>
    /// <exception cref="GuardianLinkNotPendingError">The link was already confirmed or revoked.</exception>
    /// <exception cref="InvalidLinkTokenError">The code expired before it was used.</exception>
    public void Verify(DateTime nowUtc)
    {
        // Checked before expiry, so a link confirmed earlier answers "already confirmed" however old the code now is.
        if (Status != GuardianLinkStatus.Pending)
            throw new GuardianLinkNotPendingError();

        if (nowUtc >= VerificationExpiresAt)
            throw new InvalidLinkTokenError("it has expired; ask for the candidate to be linked again");

        Status = GuardianLinkStatus.Verified;
        VerifiedAt = nowUtc;
        UpdatedAt = nowUtc;
    }

    public void Revoke()
    {
        if (Status == GuardianLinkStatus.Revoked)
            throw new GuardianLinkAlreadyRevokedError();

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
