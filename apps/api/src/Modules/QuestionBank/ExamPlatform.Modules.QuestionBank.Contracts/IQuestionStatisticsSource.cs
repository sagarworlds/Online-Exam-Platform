namespace ExamPlatform.Modules.QuestionBank.Contracts;

/// <summary>How candidates have answered one question (FR-9).</summary>
/// <param name="Answered">In how many finished attempts a candidate answered it. Attempts an administrator invalidated do not count.</param>
/// <param name="Correct">How many of those answers chose exactly the options that were correct at the version the attempt sat.</param>
/// <param name="Chosen">How many times each option was chosen, by option id; an option nobody chose is absent.</param>
public sealed record QuestionAnswerStatistics(int Answered, int Correct, IReadOnlyDictionary<Guid, int> Chosen);

/// <summary>
/// Something outside the bank that knows how candidates answered. The bank does not depend on the module that runs attempts, so that
/// module contributes this (ADR 0001), the same way it says where questions are in use.
/// </summary>
public interface IQuestionStatisticsSource
{
    /// <summary>Reads how candidates have answered a question.</summary>
    /// <param name="questionId">The question.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<QuestionAnswerStatistics> ReadAsync(Guid questionId, CancellationToken cancellationToken);
}
