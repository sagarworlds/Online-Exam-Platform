using ExamPlatform.Modules.QuestionBank.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Contracts;

namespace ExamPlatform.Modules.QuestionBank.Application;

/// <summary>The <see cref="IBookCatalog"/> other modules read books and chapters through.</summary>
public sealed class BookCatalog(IBookRepository books) : IBookCatalog
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<BookSnapshot>> GetBooksAsync(IReadOnlyCollection<Guid> bookIds, CancellationToken cancellationToken)
    {
        if (bookIds.Count == 0)
            return [];

        var found = await books.GetByIdsAsync(bookIds, cancellationToken);
        return found
            .Select(b => new BookSnapshot(
                b.Id,
                b.Name,
                b.IsArchived,
                b.Chapters.Select(c => new ChapterSnapshot(c.Id, c.BookId, c.Title, c.Order, c.IsArchived)).ToList()))
            .ToList();
    }
}
