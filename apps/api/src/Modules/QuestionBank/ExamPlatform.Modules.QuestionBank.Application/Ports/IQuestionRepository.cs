using ExamPlatform.Modules.QuestionBank.Domain;

namespace ExamPlatform.Modules.QuestionBank.Application.Ports;

/// <summary>Narrows a question listing. All parts that are set must match.</summary>
/// <param name="BookId">Only questions filed under a chapter of this book.</param>
/// <param name="ChapterId">Only questions filed under this chapter.</param>
/// <param name="UnfiledOnly">Only questions that are not filed under any chapter.</param>
public sealed record QuestionFilter(Guid? BookId = null, Guid? ChapterId = null, bool UnfiledOnly = false);

/// <summary>Persistence port for <see cref="Question"/>.</summary>
public interface IQuestionRepository
{
    /// <summary>Starts tracking a new question; it is stored when the unit of work saves.</summary>
    /// <param name="question">The question to add.</param>
    void Add(Question question);

    /// <summary>Marks a question and its options for deletion; they are removed when the unit of work saves.</summary>
    /// <param name="question">The question to remove.</param>
    void Remove(Question question);

    /// <summary>Loads one question with its options.</summary>
    /// <param name="questionId">The question's id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The question, or <see langword="null"/> when none has that id.</returns>
    Task<Question?> GetByIdAsync(Guid questionId, CancellationToken cancellationToken);

    /// <summary>Loads several questions with their options in one query.</summary>
    /// <param name="questionIds">The ids to load; unknown ids are skipped.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<Question>> GetManyAsync(IReadOnlyCollection<Guid> questionIds, CancellationToken cancellationToken);

    /// <summary>Loads several questions without their options, tracked so changes to them are saved.</summary>
    /// <param name="questionIds">The ids to load; unknown ids are skipped.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<Question>> GetManyForUpdateAsync(IReadOnlyCollection<Guid> questionIds, CancellationToken cancellationToken);

    /// <summary>Lists the most recently created questions that match the filter, newest first.</summary>
    /// <param name="filter">Which questions to include.</param>
    /// <param name="skip">How many of the newest to leave out, to reach a later page; 0 for the first.</param>
    /// <param name="take">How many to return at most.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<Question>> ListNewestAsync(QuestionFilter filter, int skip, int take, CancellationToken cancellationToken);

    /// <summary>Counts the questions filed under each chapter.</summary>
    /// <param name="chapterIds">The chapters to count for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The count per chapter; a chapter with no questions is absent.</returns>
    Task<IReadOnlyDictionary<Guid, int>> CountByChapterAsync(IReadOnlyCollection<Guid> chapterIds, CancellationToken cancellationToken);
}
