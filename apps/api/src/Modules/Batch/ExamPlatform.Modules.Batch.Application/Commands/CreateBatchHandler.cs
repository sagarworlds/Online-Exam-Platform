using MediatR;
using BatchAggregate = ExamPlatform.Modules.Batch.Domain.Batch;
using ExamPlatform.Modules.Batch.Application.Dtos;
using ExamPlatform.Modules.Batch.Application.Ports;

namespace ExamPlatform.Modules.Batch.Application.Commands;

/// Handler for CreateBatchCommand.
public class CreateBatchHandler(IBatchRepository repository, IBatchUnitOfWork unitOfWork) : IRequestHandler<CreateBatchCommand, BatchDto>
{
    public async Task<BatchDto> Handle(CreateBatchCommand command, CancellationToken cancellationToken)
    {
        if (command.MaxMembers <= 0)
            throw new ArgumentException("MaxMembers must be greater than zero", nameof(command.MaxMembers));

        var batch = new BatchAggregate(
            command.ExamId,
            command.Name,
            command.Description,
            command.MaxMembers,
            command.CreatedBy
        );

        repository.Add(batch);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToDto(batch);
    }

    private static BatchDto MapToDto(BatchAggregate batch) =>
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

/// Handler for AddBatchMemberCommand.
public class AddBatchMemberHandler(IBatchRepository repository, IBatchUnitOfWork unitOfWork) : IRequestHandler<AddBatchMemberCommand>
{
    public async Task Handle(AddBatchMemberCommand command, CancellationToken cancellationToken)
    {
        var batch = await repository.GetByIdOrThrowAsync(command.BatchId, cancellationToken);
        batch.AddMember(command.Email, command.Phone);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// Handler for ActivateBatchCommand.
public class ActivateBatchHandler(IBatchRepository repository, IBatchUnitOfWork unitOfWork) : IRequestHandler<ActivateBatchCommand>
{
    public async Task Handle(ActivateBatchCommand command, CancellationToken cancellationToken)
    {
        var batch = await repository.GetByIdOrThrowAsync(command.BatchId, cancellationToken);
        batch.Activate();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// Handler for CloseBatchCommand.
public class CloseBatchHandler(IBatchRepository repository, IBatchUnitOfWork unitOfWork) : IRequestHandler<CloseBatchCommand>
{
    public async Task Handle(CloseBatchCommand command, CancellationToken cancellationToken)
    {
        var batch = await repository.GetByIdOrThrowAsync(command.BatchId, cancellationToken);
        batch.Close();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
