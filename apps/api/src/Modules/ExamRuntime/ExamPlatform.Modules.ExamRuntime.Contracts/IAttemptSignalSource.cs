namespace ExamPlatform.Modules.ExamRuntime.Contracts;

/// <summary>
/// What other modules may read about the attempts sat at an exam, for the risk review of FR-27 (ADR 0001). The reviewing module
/// receives facts only: how an attempt went and what the candidate answered, never the answer key, which stays with the exam.
/// Consumed through this Contracts project only, never through Exam Runtime's Domain, Application or Infrastructure.
/// </summary>
public interface IAttemptSignalSource
{
    /// <summary>
    /// Lists the finished attempts at an exam without reading their answers: the cheap list a reviewer's scope is decided from, before
    /// any answer is read. An attempt is finished once it is submitted, whether or not it has since been invalidated.
    /// </summary>
    /// <param name="examId">The exam.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One entry per finished attempt, in attempt-number order; empty when the exam does not exist or nobody has finished it.</returns>
    Task<IReadOnlyList<FinishedAttemptRef>> ListFinishedAttemptRefsAsync(Guid examId, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the signals of the given finished attempts at an exam. Only the attempts named are read: an attempt that is not finished, or
    /// that belongs to another exam, is left out.
    /// </summary>
    /// <param name="examId">The exam.</param>
    /// <param name="attemptIds">The attempts to read. Empty reads nothing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One entry per attempt read, in attempt-number order.</returns>
    /// <exception cref="Exception">
    /// An attempt holds a saved answer for an option its question no longer has, so it cannot be marked. The exam's content must be
    /// fixed before the review can run; the implementation fails rather than guessing a mark.
    /// </exception>
    Task<IReadOnlyList<AttemptSignals>> ListFinishedAttemptsAsync(Guid examId, IReadOnlyCollection<Guid> attemptIds, CancellationToken cancellationToken);
}
