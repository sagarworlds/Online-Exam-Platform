namespace ExamPlatform.Modules.QuestionBank.Contracts;

/// <summary>
/// The subject each question is filed under (FR-35): the subject of its book, such as "Maths". The leaderboards rank candidates by subject
/// through this, without reading the bank's tables. Consumed through this Contracts project only.
/// </summary>
public interface IQuestionSubjects
{
    /// <summary>Finds the subject of each of the given questions that has one.</summary>
    /// <remarks>
    /// A question not filed under a chapter, or filed under a book with no subject, is absent from the result: it belongs to no subject board.
    /// </remarks>
    /// <param name="questionIds">The questions to look up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The subject by question id; a question with no subject is absent.</returns>
    Task<IReadOnlyDictionary<Guid, string>> GetSubjectsAsync(IReadOnlyCollection<Guid> questionIds, CancellationToken cancellationToken);
}
