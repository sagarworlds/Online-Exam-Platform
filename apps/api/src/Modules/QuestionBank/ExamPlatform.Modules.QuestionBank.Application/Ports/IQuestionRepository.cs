using ExamPlatform.Modules.QuestionBank.Domain;

namespace ExamPlatform.Modules.QuestionBank.Application.Ports;

/// <summary>Narrows a question listing. All parts that are set must match.</summary>
/// <param name="BookId">Only questions filed under a chapter of this book.</param>
/// <param name="ChapterId">Only questions filed under this chapter.</param>
/// <param name="UnfiledOnly">Only questions that are not filed under any chapter.</param>
/// <param name="Difficulty">Only questions of this difficulty.</param>
/// <param name="Topic">Only questions that carry this topic, already normalized (see <see cref="Question.NormalizeTopic"/>).</param>
/// <param name="Search">Only questions whose text, or any option's text, contains this; case does not matter.</param>
/// <param name="ChapterIds">Only questions filed under one of these chapters; null or empty sets no limit.</param>
/// <param name="Statuses">Only questions in one of these review statuses (FR-8); null or empty sets no limit.</param>
public sealed record QuestionFilter(
    Guid? BookId = null, Guid? ChapterId = null, bool UnfiledOnly = false, QuestionDifficulty? Difficulty = null, string? Topic = null,
    string? Search = null, IReadOnlyList<Guid>? ChapterIds = null, IReadOnlyList<QuestionStatus>? Statuses = null);

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

    /// <summary>Starts tracking a line of a question's review thread; it is stored when the unit of work saves.</summary>
    /// <param name="entry">The entry to add.</param>
    void AddReviewEntry(QuestionReviewEntry entry);

    /// <summary>A question's review thread, oldest first.</summary>
    /// <param name="questionId">The question.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<QuestionReviewEntry>> ListReviewEntriesAsync(Guid questionId, CancellationToken cancellationToken);

    /// <summary>The number of the version in force for each question; a question with no stored version is on version 1.</summary>
    /// <param name="questionIds">The questions to look up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyDictionary<Guid, int>> GetCurrentVersionNumbersAsync(IReadOnlyCollection<Guid> questionIds, CancellationToken cancellationToken);

    /// <summary>The stored versions with the given question and number; one that was never stored is simply absent.</summary>
    /// <param name="versions">The question and version number of each wanted version.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<QuestionVersion>> GetVersionsAsync(IReadOnlyCollection<(Guid QuestionId, int VersionNumber)> versions, CancellationToken cancellationToken);

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

    /// <summary>Finds where the questions that match the filter are filed, newest first, without loading them.</summary>
    /// <param name="filter">Which questions to include.</param>
    /// <param name="take">How many to return at most.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<(Guid Id, Guid? ChapterId)>> FindPlacementsAsync(QuestionFilter filter, int take, CancellationToken cancellationToken);

    /// <summary>Lists every topic any question carries, once each, in alphabetical order.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<string>> ListTopicsAsync(CancellationToken cancellationToken);

    /// <summary>Counts the questions filed under each chapter.</summary>
    /// <param name="chapterIds">The chapters to count for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The count per chapter; a chapter with no questions is absent.</returns>
    Task<IReadOnlyDictionary<Guid, int>> CountByChapterAsync(IReadOnlyCollection<Guid> chapterIds, CancellationToken cancellationToken);
}
