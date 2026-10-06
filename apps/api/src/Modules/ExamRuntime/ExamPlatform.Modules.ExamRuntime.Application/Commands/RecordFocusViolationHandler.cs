using ExamPlatform.Modules.ExamRuntime.Application.Dtos;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Domain.Exceptions;

namespace ExamPlatform.Modules.ExamRuntime.Application.Commands;

/// <summary>
/// Records that a candidate left the exam page, and ends the attempt when that was once too often (FR-22). The limit is the exam's;
/// the page only reports what the browser told it and shows the count it gets back.
/// </summary>
public sealed class RecordFocusViolationHandler(AttemptAccess access, AttemptCloser closer, IExamRuntimeUnitOfWork unitOfWork, Clock clock)
{
    /// <summary>
    /// Counts the departure and, if it reaches the exam's limit, scores and closes the attempt with what was saved. An exam that does
    /// not watch for departures records nothing, so a page left over from before the author turned the watch off is harmless.
    /// </summary>
    /// <param name="attemptId">The attempt.</param>
    /// <param name="candidateId">The signed-in candidate.</param>
    /// <param name="kind">How the candidate left the page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="AttemptNotFoundError">No such attempt, or it is someone else's.</exception>
    /// <exception cref="AttemptNotInProgressError">The attempt is already submitted, or its time has just run out and it was closed.</exception>
    /// <exception cref="ExamContentUnavailableError">The exam's questions cannot be read, so the attempt cannot be marked.</exception>
    /// <exception cref="ConcurrencyConflictError">Another request closed the attempt at the same moment.</exception>
    public async Task<FocusViolationResultDto> HandleAsync(Guid attemptId, Guid candidateId, FocusViolationKind kind, CancellationToken cancellationToken)
    {
        var (attempt, exam) = await access.LoadOwnedAsync(attemptId, candidateId, cancellationToken);

        var limit = exam.FocusViolationLimit;
        if (limit <= 0)
        {
            // Still refuse a closed attempt the way every other change does, so the page learns the sitting is over.
            if (attempt.Status != AttemptStatus.InProgress)
                throw new AttemptNotInProgressError();

            return new FocusViolationResultDto(attempt.FocusViolations.Count, 0, AttemptEnded: false);
        }

        var count = attempt.RecordFocusViolation(kind, clock.UtcNow);
        if (count >= limit)
        {
            // Saves the violation together with the close, so the attempt never ends without the record that ended it.
            await closer.CloseForViolationsAsync(attempt, exam, cancellationToken);
            return new FocusViolationResultDto(count, limit, AttemptEnded: true);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new FocusViolationResultDto(count, limit, AttemptEnded: false);
    }
}
