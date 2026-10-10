using ExamPlatform.Modules.QuestionBank.Domain;

namespace ExamPlatform.Modules.QuestionBank.Application.Ports;

/// <summary>Where a question is filed: its chapter, and the book that chapter belongs to. A read model for labels and lookups.</summary>
/// <param name="ChapterId">The chapter's id.</param>
/// <param name="ChapterTitle">The chapter's title.</param>
/// <param name="BookId">The book's id.</param>
/// <param name="BookName">The book's name.</param>
/// <param name="ClassId">The class the book belongs to, or null when it has none.</param>
/// <param name="ClassName">That class's name, or null.</param>
public sealed record ChapterRef(
    Guid ChapterId, string ChapterTitle, Guid BookId, string BookName, Guid? ClassId = null, string? ClassName = null, string? BookSubject = null);

/// <summary>Persistence port for <see cref="Book"/> and the chapters it owns.</summary>
public interface IBookRepository
{
    /// <summary>Starts tracking a new book; it is stored when the unit of work saves.</summary>
    /// <param name="book">The book to add.</param>
    void Add(Book book);

    /// <summary>Loads one book with its chapters.</summary>
    /// <param name="bookId">The book's id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The book, or <see langword="null"/> when none has that id.</returns>
    Task<Book?> GetByIdAsync(Guid bookId, CancellationToken cancellationToken);

    /// <summary>Loads several books with their chapters in one query.</summary>
    /// <param name="bookIds">The ids to load; unknown ids are skipped.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<Book>> GetByIdsAsync(IReadOnlyCollection<Guid> bookIds, CancellationToken cancellationToken);

    /// <summary>Loads the book that owns a chapter, with all its chapters.</summary>
    /// <param name="chapterId">The chapter's id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The book, or <see langword="null"/> when no chapter has that id.</returns>
    Task<Book?> GetByChapterIdAsync(Guid chapterId, CancellationToken cancellationToken);

    /// <summary>Lists books with their chapters, ordered by name.</summary>
    /// <param name="includeArchived">Whether archived books are included.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<Book>> ListAsync(bool includeArchived, CancellationToken cancellationToken);

    /// <summary>Looks up where chapters sit, in one query.</summary>
    /// <param name="chapterIds">The chapters to look up; unknown ids are skipped.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The chapters found, by chapter id.</returns>
    Task<IReadOnlyDictionary<Guid, ChapterRef>> GetChapterRefsAsync(IReadOnlyCollection<Guid> chapterIds, CancellationToken cancellationToken);
}
