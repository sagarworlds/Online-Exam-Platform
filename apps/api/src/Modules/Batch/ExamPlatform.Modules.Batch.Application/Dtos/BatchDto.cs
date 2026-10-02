using ExamPlatform.Modules.Batch.Domain;

namespace ExamPlatform.Modules.Batch.Application.Dtos;

/// DTO for batch details.
public record BatchDto(
    Guid Id,
    Guid ExamId,
    string Name,
    string? Description,
    BatchStatus Status,
    int MaxMembers,
    int ActiveMemberCount,
    Guid CreatedBy,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

/// DTO for batch member.
public record BatchMemberDto(
    Guid Id,
    string Email,
    string? Phone,
    MemberRegistrationStatus Status
);
