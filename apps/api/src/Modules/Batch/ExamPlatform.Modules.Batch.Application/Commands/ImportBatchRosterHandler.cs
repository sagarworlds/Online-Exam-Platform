using ExamPlatform.Modules.Batch.Application.Dtos;
using ExamPlatform.Modules.Batch.Application.Ports;
using ExamPlatform.Modules.Batch.Domain;
using ExamPlatform.Modules.Batch.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;
using BatchAggregate = ExamPlatform.Modules.Batch.Domain.Batch;

namespace ExamPlatform.Modules.Batch.Application.Commands;

/// <summary>Gives the candidates in a CSV roster seats in a batch (FR-50).</summary>
/// <param name="BatchId">The batch to add the candidates to.</param>
/// <param name="Content">The CSV text: an e-mail address and optionally a phone number per row, with an optional header row.</param>
public sealed record ImportBatchRosterCommand(Guid BatchId, string Content);

/// <summary>
/// Handles <see cref="ImportBatchRosterCommand"/>. Each row is judged by the batch itself
/// (<see cref="BatchAggregate.ImportMembers"/>), under the same rules a single add follows, so an imported candidate can
/// never be one the add form would have refused. A bad row does not stop the ones around it: the rows that pass are
/// saved together in one write and the caller gets a report of every row that did not, rather than an all-or-nothing
/// failure that cannot say which row was the problem. A file that cannot be read at all, or is too large, is refused
/// whole instead of being run partway.
/// </summary>
public sealed class ImportBatchRosterHandler(IBatchRepository repository, IBatchUnitOfWork unitOfWork, Clock clock)
{
    /// <summary>The most rows one import takes. It keeps one request's work bounded; a larger roster is imported in parts.</summary>
    public const int MaxRows = 1000;

    /// <summary>Reads the file, seats every row it can and reports on all of them.</summary>
    /// <param name="command">The batch and the file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="BatchNotFoundError">No batch has that id.</exception>
    /// <exception cref="RosterUnreadableError">The file has no rows, or its header has no e-mail column.</exception>
    /// <exception cref="RosterTooLargeError">The file has more than <see cref="MaxRows"/> rows.</exception>
    /// <exception cref="DuplicateMemberError">Another request gave one of the addresses a seat between the read and the save; nothing from this file was saved, and importing it again reports that address as already in the batch.</exception>
    public async Task<ImportBatchRosterResultDto> HandleAsync(ImportBatchRosterCommand command, CancellationToken cancellationToken)
    {
        var batch = await repository.GetByIdAsync(command.BatchId, cancellationToken)
            ?? throw new BatchNotFoundError(command.BatchId);

        var file = RosterFile.Read(command.Content);
        if (file.Rows.Count > MaxRows)
            throw new RosterTooLargeError(MaxRows);

        var results = batch.ImportMembers(file.Rows, clock.UtcNow);

        // Nothing to save when no row was seated, so a file of only bad rows costs a read and no write.
        if (results.Any(r => r.Outcome == RosterRowOutcome.Added))
            await unitOfWork.SaveChangesAsync(cancellationToken);

        return ImportBatchRosterResultDto.From(results, file.IgnoredColumns);
    }
}
