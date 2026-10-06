namespace ExamPlatform.Modules.QuestionBank.Contracts;

/// <summary>
/// What other modules may ask the question bank (ADR 0001): the exam builder checks that a
/// question exists, and exam delivery shows a question and marks the answers to it. Consumed through
/// this Contracts project only, never through the bank's Domain, Application or Infrastructure.
/// </summary>
public interface IQuestionBank
{
    /// <summary>Reads questions by id, answer key included.</summary>
    /// <remarks>
    /// The snapshots carry which option is correct, so a caller that shows a question to a candidate
    /// must drop <see cref="QuestionOptionSnapshot.IsCorrect"/> before it leaves the server.
    /// </remarks>
    /// <param name="questionIds">The questions to read; ids that match no question are simply absent from the result.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The questions found, in no particular order.</returns>
    Task<IReadOnlyList<QuestionSnapshot>> GetAsync(IReadOnlyCollection<Guid> questionIds, CancellationToken cancellationToken);

    /// <summary>
    /// Reads questions as they were at a given version (FR-7), so an attempt keeps showing and marking the content it was sitting
    /// however the question is edited afterwards.
    /// </summary>
    /// <param name="versions">Each question and the version wanted; a null version, or one that was never stored, reads the current content.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One snapshot per question found, carrying the version it is the content of. Where it is filed is always where it is now.</returns>
    Task<IReadOnlyList<QuestionSnapshot>> GetVersionsAsync(IReadOnlyCollection<QuestionVersionRef> versions, CancellationToken cancellationToken);

    /// <summary>Finds the questions that match the criteria, without their text or answer key.</summary>
    /// <param name="criteria">What the questions must match; nothing set matches every question.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Where each match is filed, newest first, at most <see cref="MaxFound"/> of them.</returns>
    Task<IReadOnlyList<FoundQuestion>> FindAsync(QuestionCriteria criteria, CancellationToken cancellationToken);

    /// <summary>The most questions <see cref="FindAsync"/> returns, so a draw from a huge bank stays one cheap query.</summary>
    const int MaxFound = 5000;
}
