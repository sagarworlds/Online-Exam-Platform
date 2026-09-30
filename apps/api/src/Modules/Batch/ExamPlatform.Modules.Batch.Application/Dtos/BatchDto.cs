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

/// Request to create a batch.
public record CreateBatchRequest(
    Guid ExamId,
    string Name,
    string? Description,
    int MaxMembers
);

/// DTO for batch member.
public record BatchMemberDto(
    Guid Id,
    string Email,
    string? Phone,
    MemberRegistrationStatus Status
);
