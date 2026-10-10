using ExamPlatform.Modules.Guardian.Domain;

namespace ExamPlatform.Modules.Guardian.Application.Dtos;

/// DTO for guardian details.
public record GuardianDto(
    Guid Id,
    string Email,
    string? Phone,
    string FullName,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

/// DTO for guardian link to candidate.
public record GuardianLinkDto(
    Guid Id,
    Guid GuardianId,
    Guid CandidateId,
    string CandidateEmail,
    GuardianLinkStatus Status,
    DateTime? VerifiedAt,
    DateTime? RevokedAt
)
{
    /// <summary>Maps a link to its DTO. A revoked link maps too, since its status is part of what is shown.</summary>
    /// <param name="link">The link to map.</param>
    public static GuardianLinkDto From(GuardianLink link) =>
        new(
            link.Id,
            link.GuardianId,
            link.CandidateId,
            link.CandidateEmail,
            link.Status,
            link.VerifiedAt,
            link.RevokedAt
        );
}

/// <summary>The outcome of linking a candidate: the pending link, and whether the guardian was e-mailed the confirmation request.</summary>
/// <param name="Link">The pending link.</param>
/// <param name="ConsentRequestSent">False when no mail server is configured or it refused the message.</param>
/// <param name="ConsentLink">The confirmation page, handed back only when nothing was e-mailed, so staff can pass it on.</param>
public sealed record LinkCandidateResult(GuardianLinkDto Link, bool ConsentRequestSent, string? ConsentLink);
