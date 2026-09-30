using ExamPlatform.SharedKernel.Domain;
using ExamPlatform.Modules.Guardian.Domain.Events;

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

    public Guardian(string email, string fullName, string? phone = null)
        : base(Guid.NewGuid())
    {
        Email = email;
        FullName = fullName;
        Phone = phone;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;

        AddDomainEvent(new GuardianCreatedEvent(Id, Email, FullName));
    }

    public GuardianLink LinkCandidate(Guid candidateId, string candidateEmail, string verificationToken)
    {
        var existingLink = _candidateLinks.FirstOrDefault(l => l.CandidateId == candidateId && !l.IsDeleted);
        if (existingLink != null)
            throw new InvalidOperationException("Guardian is already linked to this candidate.");

        var link = new GuardianLink(Id, candidateId, candidateEmail, verificationToken);
        _candidateLinks.Add(link);
        UpdatedAt = DateTime.UtcNow;

        return link;
    }

    public GuardianLink? GetCandidateLink(Guid candidateId) =>
        _candidateLinks.FirstOrDefault(l => l.CandidateId == candidateId && !l.IsDeleted);

    public GuardianLink? GetCandidateLinkByVerificationToken(string token) =>
        _candidateLinks.FirstOrDefault(l => l.VerificationToken == token && !l.IsDeleted);

    public void UnlinkCandidate(Guid candidateId)
    {
        var link = _candidateLinks.FirstOrDefault(l => l.CandidateId == candidateId && !l.IsDeleted);
        if (link != null)
        {
            link.SoftDelete();
            UpdatedAt = DateTime.UtcNow;
        }
    }

    public void SoftDelete()
    {
        IsDeleted = true;
        UpdatedAt = DateTime.UtcNow;
    }
}
