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
);

/// Request to create/register a guardian.
public record CreateGuardianRequest(
    string Email,
    string FullName,
    string? Phone = null
);

/// Request to link a guardian to a candidate.
public record LinkCandidateRequest(
    Guid CandidateId,
    string CandidateEmail
);

/// Request to verify a guardian link.
public record VerifyGuardianLinkRequest(
    string VerificationToken
);
