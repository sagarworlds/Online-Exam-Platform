using ExamPlatform.Modules.Batch.Application.Dtos;
using ExamPlatform.Modules.Batch.Application.Ports;
using BatchAggregate = ExamPlatform.Modules.Batch.Domain.Batch;

namespace ExamPlatform.Modules.Batch.Application.Commands;

/// <summary>Handles <see cref="CreateBatchCommand"/>: creates a new batch aggregate.</summary>
public sealed class CreateBatchHandler(IBatchRepository repository, IBatchUnitOfWork unitOfWork)
{
    /// <summary>Creates the batch and persists it.</summary>
    /// <param name="command">The batch to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created batch.</returns>
    /// <exception cref="ArgumentException"><see cref="CreateBatchCommand.MaxMembers"/> is not greater than zero.</exception>
    public async Task<BatchDto> HandleAsync(CreateBatchCommand command, CancellationToken cancellationToken)
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

/// <summary>Handles <see cref="AddBatchMemberCommand"/>: adds a member to an existing batch.</summary>
public sealed class AddBatchMemberHandler(IBatchRepository repository, IBatchUnitOfWork unitOfWork)
{
    /// <summary>Adds the member to the batch and persists the change.</summary>
    /// <param name="command">The batch and the member to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task HandleAsync(AddBatchMemberCommand command, CancellationToken cancellationToken)
    {
        var batch = await repository.GetByIdOrThrowAsync(command.BatchId, cancellationToken);
        batch.AddMember(command.Email, command.Phone);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Handles <see cref="ActivateBatchCommand"/>: moves a batch out of draft.</summary>
public sealed class ActivateBatchHandler(IBatchRepository repository, IBatchUnitOfWork unitOfWork)
{
    /// <summary>Activates the batch and persists the change.</summary>
    /// <param name="command">The batch to activate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task HandleAsync(ActivateBatchCommand command, CancellationToken cancellationToken)
    {
        var batch = await repository.GetByIdOrThrowAsync(command.BatchId, cancellationToken);
        batch.Activate();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Handles <see cref="CloseBatchCommand"/>: closes a batch for further changes.</summary>
public sealed class CloseBatchHandler(IBatchRepository repository, IBatchUnitOfWork unitOfWork)
{
    /// <summary>Closes the batch and persists the change.</summary>
    /// <param name="command">The batch to close.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task HandleAsync(CloseBatchCommand command, CancellationToken cancellationToken)
    {
        var batch = await repository.GetByIdOrThrowAsync(command.BatchId, cancellationToken);
        batch.Close();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
