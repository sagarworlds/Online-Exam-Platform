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
}
