namespace ExamPlatform.Modules.QuestionBank.Contracts;

/// <summary>One reason a question may not be moved to a chapter.</summary>
/// <param name="QuestionId">The question that may not move.</param>
/// <param name="Reason">Words for the author to read, such as which exam objects and why.</param>
public sealed record PlacementObjection(Guid QuestionId, string Reason);

/// <summary>
/// Something outside the bank that can object to a question being filed under a different chapter. The bank owns where a
/// question is filed but not what depends on it: an exam limited to a book or chapters would break its own rule if a question
/// it holds moved out of that scope. Each module with such a rule contributes one of these (ADR 0001), so the bank can ask
/// without knowing the rule.
/// </summary>
public interface IQuestionPlacementGuard
{
    /// <summary>Checks whether the questions may be filed under the chapter.</summary>
    /// <param name="questionIds">The questions about to move.</param>
    /// <param name="bookId">The book the new chapter belongs to.</param>
    /// <param name="chapterId">The new chapter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One objection per question that may not move; empty when all may.</returns>
    Task<IReadOnlyList<PlacementObjection>> CheckAsync(
        IReadOnlyCollection<Guid> questionIds, Guid bookId, Guid chapterId, CancellationToken cancellationToken);
}
