using ExamPlatform.Modules.QuestionBank.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Domain;
using Microsoft.EntityFrameworkCore;

namespace ExamPlatform.Modules.QuestionBank.Infrastructure.Repositories;

/// <summary>EF Core-backed <see cref="IBookRepository"/>.</summary>
public sealed class BookRepository(QuestionBankDbContext context) : IBookRepository
{
    /// <inheritdoc />
    public void Add(Book book) => context.Books.Add(book);

    /// <inheritdoc />
    public Task<Book?> GetByIdAsync(Guid bookId, CancellationToken cancellationToken) =>
        context.Books.Include(b => b.Chapters).FirstOrDefaultAsync(b => b.Id == bookId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Book>> GetByIdsAsync(IReadOnlyCollection<Guid> bookIds, CancellationToken cancellationToken) =>
        await context.Books.AsNoTracking().Include(b => b.Chapters)
            .Where(b => bookIds.Contains(b.Id))
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<Book?> GetByChapterIdAsync(Guid chapterId, CancellationToken cancellationToken) =>
        context.Books.Include(b => b.Chapters)
            .FirstOrDefaultAsync(b => context.Chapters.Any(c => c.Id == chapterId && c.BookId == b.Id), cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Book>> ListAsync(bool includeArchived, CancellationToken cancellationToken) =>
        await context.Books.AsNoTracking().Include(b => b.Chapters)
            .Where(b => includeArchived || !b.IsArchived)
            .OrderBy(b => b.Name).ThenBy(b => b.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, ChapterRef>> GetChapterRefsAsync(
        IReadOnlyCollection<Guid> chapterIds, CancellationToken cancellationToken)
    {
        if (chapterIds.Count == 0)
            return new Dictionary<Guid, ChapterRef>();

        return await context.Chapters.AsNoTracking()
            .Where(c => chapterIds.Contains(c.Id))
            .Join(context.Books, c => c.BookId, b => b.Id, (c, b) => new ChapterRef(c.Id, c.Title, b.Id, b.Name))
            .ToDictionaryAsync(r => r.ChapterId, cancellationToken);
    }
}
