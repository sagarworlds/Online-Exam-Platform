namespace ExamPlatform.Modules.QuestionBank.Contracts;

/// <summary>
/// Rescoring after QuestionBank corrects a question's answer key (ADR 0001, FR-31): the bank does not know what a "score" or
/// an "attempt" is, so the module that does — ExamRuntime — contributes this, the same way it contributes
/// <see cref="IQuestionUsageSource"/>. Called once per correction, after the question's new key is saved.
/// </summary>
public interface IAttemptRescorer
{
    /// <summary>
    /// Recomputes the score of every submitted attempt that included this question, under its corrected answer key, and
    /// records what changed so a candidate whose score moves can see why.
    /// </summary>
    /// <param name="questionId">The question whose answer key was just corrected.</param>
    /// <param name="reason">Why the key changed; shown to a candidate whose score moves because of it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many attempts' scores actually changed. An attempt the correction does not affect is not counted.</returns>
    Task<int> RescoreForQuestionAsync(Guid questionId, string reason, CancellationToken cancellationToken);
}
