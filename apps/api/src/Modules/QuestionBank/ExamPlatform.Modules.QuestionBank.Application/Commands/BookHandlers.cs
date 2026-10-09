using ExamPlatform.Modules.QuestionBank.Application.Dtos;
using ExamPlatform.Modules.QuestionBank.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Domain;
using ExamPlatform.Modules.QuestionBank.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.QuestionBank.Application.Commands;

/// <summary>Creates a book (FR-5).</summary>
/// <param name="Name">The book's name.</param>
/// <param name="Subject">The subject it covers, or null.</param>
/// <param name="Description">A short description, or null.</param>
/// <param name="CreatedBy">The authoring user, taken from the caller's token.</param>
/// <param name="ClassId">The class the book belongs to, or null for none.</param>
public sealed record CreateBookCommand(string? Name, string? Subject, string? Description, Guid CreatedBy, Guid? ClassId = null);

/// <summary>Changes a book's name, subject, description and class: the details are replaced as a whole.</summary>
/// <param name="BookId">The book to change.</param>
/// <param name="Name">The new name.</param>
/// <param name="Subject">The new subject, or null.</param>
/// <param name="Description">The new description, or null.</param>
/// <param name="ClassId">The class the book now belongs to; null takes it out of its class.</param>
public sealed record UpdateBookCommand(Guid BookId, string? Name, string? Subject, string? Description, Guid? ClassId = null);

/// <summary>Adds a chapter to a book.</summary>
/// <param name="BookId">The book to add it to.</param>
/// <param name="Title">The chapter's title.</param>
public sealed record AddChapterCommand(Guid BookId, string? Title);

/// <summary>Renames a chapter.</summary>
/// <param name="BookId">The book the chapter belongs to.</param>
/// <param name="ChapterId">The chapter to rename.</param>
/// <param name="Title">The new title.</param>
public sealed record RenameChapterCommand(Guid BookId, Guid ChapterId, string? Title);

/// <summary>Creates a book.</summary>
public sealed class CreateBookHandler(IBookRepository books, OpenClassResolver openClasses, IQuestionBankUnitOfWork unitOfWork, Clock clock)
{
    /// <summary>Validates and stores the book.</summary>
    /// <param name="command">The book to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stored book.</returns>
    /// <exception cref="InvalidBookError">The name is blank or a field is too long.</exception>
    /// <exception cref="ClassNotFoundError">A class is named and no class has that id.</exception>
    /// <exception cref="ClassArchivedError">The class is archived, so it takes no new books.</exception>
    public async Task<BookDto> HandleAsync(CreateBookCommand command, CancellationToken cancellationToken)
    {
        var book = Book.Create(command.Name, command.Subject, command.Description, command.CreatedBy, clock.UtcNow, command.ClassId);
        var className = await openClasses.ResolveAsync(command.ClassId, cancellationToken);

        books.Add(book);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return book.ToDto(new Dictionary<Guid, int>(), className);
    }
}

/// <summary>Runs one change on a book and returns the book as it is afterwards.</summary>
public sealed class ChangeBookHandler(
    IBookRepository books,
    IQuestionRepository questions,
    IClassRepository classes,
    OpenClassResolver openClasses,
    IQuestionBankUnitOfWork unitOfWork)
{
    /// <summary>Renames a book, or changes its subject, description or class.</summary>
    /// <param name="command">The change.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="BookNotFoundError">No book has that id.</exception>
    /// <exception cref="InvalidBookError">The name is blank or a field is too long.</exception>
    /// <exception cref="ClassNotFoundError">A class is named and no class has that id.</exception>
    /// <exception cref="ClassArchivedError">The book is being moved to a class that is archived.</exception>
    public Task<BookDto> UpdateAsync(UpdateBookCommand command, CancellationToken cancellationToken) =>
        ChangeAsync(
            command.BookId,
            async book =>
            {
                // Only a change of class has to name one that is open: a book may stay in a class that has since been archived.
                if (command.ClassId != book.ClassId)
                    await openClasses.ResolveAsync(command.ClassId, cancellationToken);

                book.Update(command.Name, command.Subject, command.Description, command.ClassId);
            },
            cancellationToken);

    /// <summary>Archives a book; it keeps its questions but takes no new chapters.</summary>
    /// <param name="bookId">The book to archive.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="BookNotFoundError">No book has that id.</exception>
    public Task<BookDto> ArchiveAsync(Guid bookId, CancellationToken cancellationToken) =>
        ChangeAsync(bookId, book => book.Archive(), cancellationToken);

    /// <summary>Brings an archived book back into use.</summary>
    /// <param name="bookId">The book to restore.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="BookNotFoundError">No book has that id.</exception>
    public Task<BookDto> RestoreAsync(Guid bookId, CancellationToken cancellationToken) =>
        ChangeAsync(bookId, book => book.Restore(), cancellationToken);

    /// <summary>Adds a chapter to the book.</summary>
    /// <param name="command">The chapter to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="BookNotFoundError">No book has that id.</exception>
    /// <exception cref="BookArchivedError">The book is archived.</exception>
    /// <exception cref="InvalidBookError">The title is blank or too long.</exception>
    /// <exception cref="DuplicateChapterError">The book already has a chapter with that title.</exception>
    public Task<BookDto> AddChapterAsync(AddChapterCommand command, CancellationToken cancellationToken) =>
        ChangeAsync(command.BookId, book => book.AddChapter(command.Title), cancellationToken);

    /// <summary>Renames a chapter.</summary>
    /// <param name="command">The rename.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="BookNotFoundError">No book has that id.</exception>
    /// <exception cref="ChapterNotFoundError">The book has no such chapter.</exception>
    /// <exception cref="InvalidBookError">The title is blank or too long.</exception>
    /// <exception cref="DuplicateChapterError">Another chapter of the book has that title.</exception>
    public Task<BookDto> RenameChapterAsync(RenameChapterCommand command, CancellationToken cancellationToken) =>
        ChangeAsync(command.BookId, book => book.RenameChapter(command.ChapterId, command.Title), cancellationToken);

    /// <summary>Archives a chapter; it keeps its questions but takes no new ones.</summary>
    /// <param name="bookId">The book the chapter belongs to.</param>
    /// <param name="chapterId">The chapter to archive.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="BookNotFoundError">No book has that id.</exception>
    /// <exception cref="ChapterNotFoundError">The book has no such chapter.</exception>
    public Task<BookDto> ArchiveChapterAsync(Guid bookId, Guid chapterId, CancellationToken cancellationToken) =>
        ChangeAsync(bookId, book => book.ArchiveChapter(chapterId), cancellationToken);

    /// <summary>Brings an archived chapter back into use.</summary>
    /// <param name="bookId">The book the chapter belongs to.</param>
    /// <param name="chapterId">The chapter to restore.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="BookNotFoundError">No book has that id.</exception>
    /// <exception cref="ChapterNotFoundError">The book has no such chapter.</exception>
    public Task<BookDto> RestoreChapterAsync(Guid bookId, Guid chapterId, CancellationToken cancellationToken) =>
        ChangeAsync(bookId, book => book.RestoreChapter(chapterId), cancellationToken);

    private Task<BookDto> ChangeAsync(Guid bookId, Action<Book> change, CancellationToken cancellationToken) =>
        ChangeAsync(bookId, book => { change(book); return Task.CompletedTask; }, cancellationToken);

    private async Task<BookDto> ChangeAsync(Guid bookId, Func<Book, Task> change, CancellationToken cancellationToken)
    {
        var book = await books.GetByIdAsync(bookId, cancellationToken) ?? throw new BookNotFoundError();

        await change(book);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var counts = await questions.CountByChapterAsync(book.Chapters.Select(c => c.Id).ToList(), cancellationToken);
        var className = book.ClassId is { } classId ? (await classes.GetNamesAsync([classId], cancellationToken)).GetValueOrDefault(classId) : null;
        return book.ToDto(counts, className);
    }
}
