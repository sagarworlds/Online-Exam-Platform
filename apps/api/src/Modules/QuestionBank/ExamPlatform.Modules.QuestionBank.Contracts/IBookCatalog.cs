namespace ExamPlatform.Modules.QuestionBank.Contracts;

/// <summary>
/// What other modules may ask about books and chapters (ADR 0001): the exam builder checks that the book or chapters
/// an exam is limited to exist and are open, and shows their names. Consumed through this Contracts project only,
/// never through the bank's Domain, Application or Infrastructure.
/// </summary>
public interface IBookCatalog
{
    /// <summary>Reads books with their chapters by id.</summary>
    /// <param name="bookIds">The books to read; ids that match no book are simply absent from the result.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The books found, in no particular order.</returns>
    Task<IReadOnlyList<BookSnapshot>> GetBooksAsync(IReadOnlyCollection<Guid> bookIds, CancellationToken cancellationToken);
}

/// <summary>A book as another module sees it.</summary>
/// <param name="Id">The book's id.</param>
/// <param name="Name">The book's name.</param>
/// <param name="IsArchived">Whether the book is archived: kept, but not to be chosen for anything new.</param>
/// <param name="Chapters">The book's chapters, in order.</param>
public sealed record BookSnapshot(Guid Id, string Name, bool IsArchived, IReadOnlyList<ChapterSnapshot> Chapters);

/// <summary>One chapter of a <see cref="BookSnapshot"/>.</summary>
/// <param name="Id">The chapter's id.</param>
/// <param name="BookId">The book it belongs to.</param>
/// <param name="Title">The chapter's title.</param>
/// <param name="Order">Position in the book, from 1.</param>
/// <param name="IsArchived">Whether the chapter is archived.</param>
public sealed record ChapterSnapshot(Guid Id, Guid BookId, string Title, int Order, bool IsArchived);
