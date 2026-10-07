using ExamPlatform.Modules.ExamRuntime.Application.Dtos;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.SharedKernel.Domain.Exceptions;

namespace ExamPlatform.Modules.ExamRuntime.Application.Commands;

/// <summary>Ends an attempt and scores it (FR-19, FR-21).</summary>
public sealed class SubmitAttemptHandler(AttemptAccess access, AttemptCloser closer, AttemptViewBuilder views, IExamRuntimeUnitOfWork unitOfWork)
{
    /// <summary>
    /// Scores and closes the attempt. Submitting one that is already over returns its result unchanged, so a
    /// double click, or a retry after a dropped response, is harmless.
    /// </summary>
    /// <param name="attemptId">The attempt.</param>
    /// <param name="candidateId">The signed-in candidate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="AttemptNotFoundError">No such attempt, or it is someone else's.</exception>
    /// <exception cref="ExamContentUnavailableError">The exam's questions cannot be read, so it cannot be marked.</exception>
    /// <exception cref="ConcurrencyConflictError">Another request closed the attempt at the same moment.</exception>
    public async Task<AttemptDto> HandleAsync(Guid attemptId, Guid candidateId, CancellationToken cancellationToken)
    {
        // Held from before the attempt is loaded until it is closed: an answer being saved at this moment either finishes first, and is
        // scored, or waits and is refused because the attempt is over. Either way the score is of the answers that are stored.
        await using var hold = await unitOfWork.LockAttemptAsync(attemptId, exclusive: true, cancellationToken);
        var (attempt, exam) = await access.LoadOwnedAsync(attemptId, candidateId, cancellationToken);

        if (attempt.Status == AttemptStatus.InProgress)
            await closer.CloseAsync(attempt, exam, cancellationToken);
        await hold.CompleteAsync(cancellationToken);

        return await views.BuildAsync(attempt, exam, cancellationToken);
    }
}
