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
