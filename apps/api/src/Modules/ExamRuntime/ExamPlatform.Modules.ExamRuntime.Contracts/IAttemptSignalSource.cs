namespace ExamPlatform.Modules.ExamRuntime.Contracts;

/// <summary>
/// What other modules may read about the attempts sat at an exam, for the risk review of FR-27 (ADR 0001). The reviewing module
/// receives facts only: how an attempt went and what the candidate answered, never the answer key, which stays with the exam.
/// Consumed through this Contracts project only, never through Exam Runtime's Domain, Application or Infrastructure.
/// </summary>
public interface IAttemptSignalSource
{
    /// <summary>
    /// Reads the signals of every finished attempt at an exam: each one that was submitted, whether or not an administrator has since
    /// invalidated it. Attempts still open are left out, since their answers and timing are not final.
    /// </summary>
    /// <param name="examId">The exam.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One entry per finished attempt, in attempt-number order; empty when the exam does not exist or nobody has finished it.</returns>
    /// <exception cref="Exception">
    /// An attempt holds a saved answer for an option its question no longer has, so it cannot be marked. The exam's content must be
    /// fixed before the review can run; the implementation fails rather than guessing a mark.
    /// </exception>
    Task<IReadOnlyList<AttemptSignals>> ListFinishedAttemptsAsync(Guid examId, CancellationToken cancellationToken);
}
