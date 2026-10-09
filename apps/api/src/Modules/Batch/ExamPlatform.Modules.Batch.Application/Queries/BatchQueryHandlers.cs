using ExamPlatform.Modules.Batch.Application.Dtos;
using ExamPlatform.Modules.Batch.Application.Ports;
using ExamPlatform.Modules.Batch.Domain.Exceptions;

namespace ExamPlatform.Modules.Batch.Application.Queries;

/// <summary>Handles the request for every batch, for the batch list page (FR-17).</summary>
public sealed class ListBatchesHandler(IBatchRepository repository)
{
    /// <summary>Lists every batch, newest first, each with its count of members who still hold a seat.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<BatchDto>> HandleAsync(CancellationToken cancellationToken)
    {
        var batches = await repository.ListAsync(cancellationToken);
        return batches.Select(BatchDto.From).ToList();
    }
}

/// <summary>Handles the request for a batch's roster, for the roster page (FR-18).</summary>
public sealed class ListBatchMembersHandler(IBatchRepository repository)
{
    /// <summary>
    /// Lists the members of a batch who still hold a seat. A removed member is left out, because the roster
    /// shows who is on the batch now, and a removal is kept only so that the history can be audited.
    /// </summary>
    /// <param name="batchId">The batch whose roster to list.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="BatchNotFoundError">No batch has that id.</exception>
    public async Task<IReadOnlyList<BatchMemberDto>> HandleAsync(Guid batchId, CancellationToken cancellationToken)
    {
        var batch = await repository.GetByIdAsync(batchId, cancellationToken)
            ?? throw new BatchNotFoundError(batchId);

        return batch.Members
            .Where(member => !member.IsDeleted)
            .Select(member => new BatchMemberDto(member.Id, member.Email, member.Phone, member.Status))
            .ToList();
    }
}
