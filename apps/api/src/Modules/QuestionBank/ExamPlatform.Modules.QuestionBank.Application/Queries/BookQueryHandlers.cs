using ExamPlatform.Modules.QuestionBank.Application.Dtos;
using ExamPlatform.Modules.QuestionBank.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Domain.Exceptions;

namespace ExamPlatform.Modules.QuestionBank.Application.Queries;

/// <summary>Lists books with their chapters for the authoring screens.</summary>
public sealed class ListBooksHandler(IBookRepository books, IQuestionRepository questions)
{
    /// <summary>Returns the books ordered by name, each with its chapters and their question counts.</summary>
    /// <param name="includeArchived">Whether archived books are included.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<BookDto>> HandleAsync(bool includeArchived, CancellationToken cancellationToken)
    {
        var list = await books.ListAsync(includeArchived, cancellationToken);
        var counts = await questions.CountByChapterAsync(list.SelectMany(b => b.Chapters).Select(c => c.Id).ToList(), cancellationToken);
        return list.Select(b => b.ToDto(counts)).ToList();
    }
}

/// <summary>Reads one book with its chapters.</summary>
public sealed class GetBookHandler(IBookRepository books, IQuestionRepository questions)
{
    /// <summary>Returns the book with its chapters and their question counts.</summary>
    /// <param name="bookId">The book's id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="BookNotFoundError">No book has that id.</exception>
    public async Task<BookDto> HandleAsync(Guid bookId, CancellationToken cancellationToken)
    {
        var book = await books.GetByIdAsync(bookId, cancellationToken) ?? throw new BookNotFoundError();
        var counts = await questions.CountByChapterAsync(book.Chapters.Select(c => c.Id).ToList(), cancellationToken);
        return book.ToDto(counts);
    }
}
