using ExamPlatform.Modules.Batch.Domain;
using BatchAggregate = ExamPlatform.Modules.Batch.Domain.Batch;

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
)
{
    /// <summary>Maps a batch to its DTO, counting only the members who still hold a seat.</summary>
    /// <param name="batch">The batch to map.</param>
    public static BatchDto From(BatchAggregate batch) =>
        new(
            batch.Id,
            batch.ExamId,
            batch.Name,
            batch.Description,
            batch.Status,
            batch.MaxMembers,
            batch.GetActiveMemberCount(),
            batch.CreatedBy,
            batch.CreatedAt,
            batch.UpdatedAt
        );
}

/// DTO for batch member.
public record BatchMemberDto(
    Guid Id,
    string Email,
    string? Phone,
    MemberRegistrationStatus Status
);
