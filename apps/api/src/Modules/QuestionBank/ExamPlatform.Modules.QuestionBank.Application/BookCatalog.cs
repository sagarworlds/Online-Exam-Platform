using ExamPlatform.Modules.QuestionBank.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Contracts;

namespace ExamPlatform.Modules.QuestionBank.Application;

/// <summary>The <see cref="IBookCatalog"/> other modules read books and chapters through.</summary>
public sealed class BookCatalog(IBookRepository books, IClassRepository classes) : IBookCatalog
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<BookSnapshot>> GetBooksAsync(IReadOnlyCollection<Guid> bookIds, CancellationToken cancellationToken)
    {
        if (bookIds.Count == 0)
            return [];

        var found = await books.GetByIdsAsync(bookIds, cancellationToken);
        var names = await classes.GetNamesAsync(found.Where(b => b.ClassId is not null).Select(b => b.ClassId!.Value).Distinct().ToList(), cancellationToken);
        return found
            .Select(b => new BookSnapshot(
                b.Id,
                b.Name,
                b.IsArchived,
                b.Chapters.Select(c => new ChapterSnapshot(c.Id, c.BookId, c.Title, c.Order, c.IsArchived)).ToList(),
                b.ClassId,
                b.ClassId is { } id ? names.GetValueOrDefault(id) : null))
            .ToList();
    }
}
