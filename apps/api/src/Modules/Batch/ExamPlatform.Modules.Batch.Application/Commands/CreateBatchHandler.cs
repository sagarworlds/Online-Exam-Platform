using ExamPlatform.Modules.Batch.Application.Dtos;
using ExamPlatform.Modules.Batch.Application.Ports;
using ExamPlatform.Modules.Batch.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;
using BatchAggregate = ExamPlatform.Modules.Batch.Domain.Batch;

namespace ExamPlatform.Modules.Batch.Application.Commands;

/// <summary>Handles <see cref="CreateBatchCommand"/>: creates a new batch aggregate.</summary>
public sealed class CreateBatchHandler(IBatchRepository repository, IBatchUnitOfWork unitOfWork, Clock clock)
{
    /// <summary>Creates the batch and persists it.</summary>
    /// <param name="command">The batch to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created batch.</returns>
    /// <exception cref="InvalidBatchConfigError">The name is blank or <see cref="CreateBatchCommand.MaxMembers"/> is not greater than zero.</exception>
    public async Task<BatchDto> HandleAsync(CreateBatchCommand command, CancellationToken cancellationToken)
    {
        var batch = new BatchAggregate(
            command.ExamId,
            command.Name,
            command.Description,
            command.MaxMembers,
            command.CreatedBy,
            clock.UtcNow
        );

        repository.Add(batch);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return BatchDto.From(batch);
    }
}

/// <summary>Handles <see cref="AddBatchMemberCommand"/>: adds a member to an existing batch.</summary>
public sealed class AddBatchMemberHandler(IBatchRepository repository, IBatchUnitOfWork unitOfWork, Clock clock)
{
    /// <summary>Adds the member to the batch and persists the change.</summary>
    /// <param name="command">The batch and the member to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="BatchNotFoundError">No batch has that id.</exception>
    /// <exception cref="InvalidBatchMemberError">The address or the phone number is not usable.</exception>
    /// <exception cref="DuplicateMemberError">The address already holds a seat.</exception>
    public async Task HandleAsync(AddBatchMemberCommand command, CancellationToken cancellationToken)
    {
        var batch = await repository.GetByIdAsync(command.BatchId, cancellationToken)
            ?? throw new BatchNotFoundError(command.BatchId);
        batch.AddMember(command.Email, command.Phone, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Handles <see cref="ActivateBatchCommand"/>: moves a batch out of draft.</summary>
public sealed class ActivateBatchHandler(IBatchRepository repository, IBatchUnitOfWork unitOfWork, Clock clock)
{
    /// <summary>Activates the batch and persists the change.</summary>
    /// <param name="command">The batch to activate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="BatchNotFoundError">No batch has that id.</exception>
    public async Task HandleAsync(ActivateBatchCommand command, CancellationToken cancellationToken)
    {
        var batch = await repository.GetByIdAsync(command.BatchId, cancellationToken)
            ?? throw new BatchNotFoundError(command.BatchId);
        batch.Activate(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Handles <see cref="CloseBatchCommand"/>: closes a batch for further changes.</summary>
public sealed class CloseBatchHandler(IBatchRepository repository, IBatchUnitOfWork unitOfWork, Clock clock)
{
    /// <summary>Closes the batch and persists the change.</summary>
    /// <param name="command">The batch to close.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="BatchNotFoundError">No batch has that id.</exception>
    public async Task HandleAsync(CloseBatchCommand command, CancellationToken cancellationToken)
    {
        var batch = await repository.GetByIdAsync(command.BatchId, cancellationToken)
            ?? throw new BatchNotFoundError(command.BatchId);
        batch.Close(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
