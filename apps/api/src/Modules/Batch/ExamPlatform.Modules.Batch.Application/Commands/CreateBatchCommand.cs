using MediatR;
using ExamPlatform.Modules.Batch.Application.Dtos;

namespace ExamPlatform.Modules.Batch.Application.Commands;

/// Command to create a new batch.
public record CreateBatchCommand(
    Guid ExamId,
    string Name,
    string? Description,
    int MaxMembers,
    Guid CreatedBy
) : IRequest<BatchDto>;

/// Command to add a member to a batch.
public record AddBatchMemberCommand(
    Guid BatchId,
    string Email,
    string? Phone = null
) : IRequest;

/// Command to activate a batch.
public record ActivateBatchCommand(
    Guid BatchId
) : IRequest;

/// Command to close a batch.
public record CloseBatchCommand(
    Guid BatchId
) : IRequest;
