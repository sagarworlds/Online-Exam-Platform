using ExamPlatform.SharedKernel.Domain;
using ExamPlatform.Modules.Guardian.Domain.Events;
using ExamPlatform.Modules.Guardian.Domain.Exceptions;

namespace ExamPlatform.Modules.Guardian.Domain;


/// Guardian aggregate root (FR-35, FR-36). Represents a legal guardian managing minor candidates' exam access.
public class Guardian : AggregateRoot
{
    public new Guid Id => base.Id;
    public string Email { get; set; } = null!;
    public string? Phone { get; set; }
    public string FullName { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }

    private readonly List<GuardianLink> _candidateLinks = [];
    public IReadOnlyList<GuardianLink> CandidateLinks => _candidateLinks.AsReadOnly();

    private Guardian() : base(Guid.Empty) { }

    /// <summary>Registers a guardian.</summary>
    /// <param name="email">A valid e-mail address.</param>
    /// <param name="fullName">The guardian's name; must not be blank.</param>
    /// <param name="phone">Optional phone number.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <exception cref="InvalidGuardianDetailsError">The e-mail address is invalid or the name is blank.</exception>
    public Guardian(string email, string fullName, string? phone, DateTime nowUtc)
        : base(Guid.NewGuid())
    {
        // The rules live on the aggregate, so no caller can build a guardian that breaks them. This is the shared e-mail rule's

        // framework check, which is what a guardian was always held to: it lets an address with a space in it through, so the other

        // modules' stricter IsDeliverable is not used here without a decision to turn those addresses away.

        if (!EmailAddressRule.IsWellFormed(email))

            throw new InvalidGuardianDetailsError("Email must be a valid email address.");

        if (string.IsNullOrWhiteSpace(fullName))
            throw new InvalidGuardianDetailsError("FullName cannot be empty or whitespace.");

        Email = email;
        FullName = fullName;
        Phone = phone;
        CreatedAt = nowUtc;
        UpdatedAt = nowUtc;

        AddDomainEvent(new GuardianCreatedEvent(Id, Email, FullName));
    }

    /// <summary>Starts a pending link to a candidate, awaiting the guardian's confirmation with the code whose hash is given.</summary>
    /// <param name="candidateId">The candidate to link.</param>
    /// <param name="candidateEmail">The candidate's address, as staff entered it.</param>
    /// <param name="verificationTokenHash">The hash of the one-time code e-mailed to the guardian; the code itself is not kept.</param>
    /// <param name="verificationExpiresAt">When that code stops working.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <exception cref="GuardianAlreadyLinkedError">The guardian already has a link to the candidate.</exception>
    public GuardianLink LinkCandidate(
        Guid candidateId, string candidateEmail, string verificationTokenHash, DateTime verificationExpiresAt, DateTime nowUtc)
    {
        var existingLink = _candidateLinks.FirstOrDefault(l => l.CandidateId == candidateId && !l.IsDeleted);
        if (existingLink != null)
            throw new GuardianAlreadyLinkedError();

        var link = new GuardianLink(Id, candidateId, candidateEmail, verificationTokenHash, verificationExpiresAt, nowUtc);
        _candidateLinks.Add(link);
        UpdatedAt = nowUtc;

        return link;
    }

    public GuardianLink? GetCandidateLink(Guid candidateId) =>
        _candidateLinks.FirstOrDefault(l => l.CandidateId == candidateId && !l.IsDeleted);

    /// <summary>Confirms the link that the given code was issued for, and raises <see cref="GuardianLinkVerifiedEvent"/>.</summary>
    /// <param name="verificationTokenHash">The hash of the code the guardian presented.</param>
    /// <param name="nowUtc">The moment of confirmation.</param>
    /// <returns>The confirmed link.</returns>
    /// <exception cref="InvalidLinkTokenError">No link of this guardian was issued that code, or the code has expired.</exception>
    /// <exception cref="GuardianLinkNotPendingError">The link was already confirmed or revoked.</exception>
    public GuardianLink VerifyCandidateLink(string verificationTokenHash, DateTime nowUtc)
    {
        var link = _candidateLinks.FirstOrDefault(l => l.VerificationTokenHash == verificationTokenHash && !l.IsDeleted)
            ?? throw new InvalidLinkTokenError("it is not recognised");

        link.Verify(nowUtc);
        UpdatedAt = nowUtc;
        AddDomainEvent(new GuardianLinkVerifiedEvent(link.Id, Id, link.CandidateId));
        return link;
    }

    /// <summary>Revokes the link to a candidate, keeping it on record as revoked.</summary>
    /// <param name="candidateId">The candidate whose link is revoked.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <exception cref="GuardianLinkNotFoundError">The guardian has no link to the candidate.</exception>
    /// <exception cref="GuardianLinkAlreadyRevokedError">The link was already revoked.</exception>
    public void RevokeCandidateLink(Guid candidateId, DateTime nowUtc)
    {
        // A missing link is an error, not a quiet success: a caller who revokes the wrong pair must be told.
        var link = GetCandidateLink(candidateId) ?? throw new GuardianLinkNotFoundError();
        link.Revoke(nowUtc);
        UpdatedAt = nowUtc;
    }

    /// <summary>Removes the link to a candidate.</summary>
    /// <param name="candidateId">The candidate to unlink.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <exception cref="GuardianLinkNotFoundError">The guardian has no link to the candidate.</exception>
    public void UnlinkCandidate(Guid candidateId, DateTime nowUtc)
    {
        var link = GetCandidateLink(candidateId) ?? throw new GuardianLinkNotFoundError();
        link.SoftDelete(nowUtc);
        UpdatedAt = nowUtc;
    }

    /// <summary>Marks the guardian deleted.</summary>
    /// <param name="nowUtc">The current instant.</param>
    public void SoftDelete(DateTime nowUtc)
    {
        IsDeleted = true;
        UpdatedAt = nowUtc;
    }
}
