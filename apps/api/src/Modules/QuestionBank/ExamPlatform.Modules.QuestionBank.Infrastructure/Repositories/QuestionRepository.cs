using ExamPlatform.Modules.QuestionBank.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Domain;
using Microsoft.EntityFrameworkCore;

namespace ExamPlatform.Modules.QuestionBank.Infrastructure.Repositories;

/// <summary>EF Core-backed <see cref="IQuestionRepository"/>.</summary>
public sealed class QuestionRepository(QuestionBankDbContext context) : IQuestionRepository
{
    /// <inheritdoc />
    public void Add(Question question) => context.Questions.Add(question);

    // The options are deleted with it by the cascade the model declares, so a removed question leaves no orphans behind.
    /// <inheritdoc />
    public void Remove(Question question) => context.Questions.Remove(question);

    /// <inheritdoc />
    public Task<Question?> GetByIdAsync(Guid questionId, CancellationToken cancellationToken) =>
        context.Questions.Include(q => q.Options).FirstOrDefaultAsync(q => q.Id == questionId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Question>> GetManyAsync(IReadOnlyCollection<Guid> questionIds, CancellationToken cancellationToken) =>
        await context.Questions.AsNoTracking().Include(q => q.Options)
            .Where(q => questionIds.Contains(q.Id))
            .ToListAsync(cancellationToken);

    // Without the options on purpose: filing changes only where a question sits, and a page of up to 200 questions does not
    // need every one of their options loaded to do that.
    /// <inheritdoc />
    public async Task<IReadOnlyList<Question>> GetManyForUpdateAsync(IReadOnlyCollection<Guid> questionIds, CancellationToken cancellationToken) =>
        await context.Questions.Where(q => questionIds.Contains(q.Id)).ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Question>> ListNewestAsync(QuestionFilter filter, int skip, int take, CancellationToken cancellationToken) =>
        // The id breaks ties between questions created in the same instant, so a page boundary never repeats or skips one.
        await Matching(context.Questions.AsNoTracking().Include(q => q.Options), filter)
            .OrderByDescending(q => q.CreatedAtUtc).ThenBy(q => q.Id).Skip(skip).Take(take).ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<(Guid Id, Guid? ChapterId)>> FindPlacementsAsync(QuestionFilter filter, int take, CancellationToken cancellationToken)
    {
        var found = await Matching(context.Questions.AsNoTracking(), filter)
            .OrderByDescending(q => q.CreatedAtUtc).ThenBy(q => q.Id).Take(take)
            .Select(q => new { q.Id, q.ChapterId })
            .ToListAsync(cancellationToken);

        return found.Select(q => (q.Id, q.ChapterId)).ToList();
    }

    private IQueryable<Question> Matching(IQueryable<Question> query, QuestionFilter filter)
    {
        if (filter.UnfiledOnly)
            query = query.Where(q => q.ChapterId == null);
        if (filter.ChapterId is { } chapterId)
            query = query.Where(q => q.ChapterId == chapterId);
        if (filter.BookId is { } bookId)
            query = query.Where(q => context.Chapters.Any(c => c.Id == q.ChapterId && c.BookId == bookId));
        if (filter.Difficulty is { } difficulty)
            query = query.Where(q => q.Difficulty == difficulty);
        if (filter.Topic is { Length: > 0 } topic)
            query = query.Where(q => q.Topics.Contains(topic));

        return query;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListTopicsAsync(CancellationToken cancellationToken) =>
        await context.Questions.AsNoTracking().SelectMany(q => q.Topics).Distinct().OrderBy(t => t).ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, int>> CountByChapterAsync(IReadOnlyCollection<Guid> chapterIds, CancellationToken cancellationToken)
    {
        if (chapterIds.Count == 0)
            return new Dictionary<Guid, int>();

        return await context.Questions.AsNoTracking()
            .Where(q => q.ChapterId != null && chapterIds.Contains(q.ChapterId.Value))
            .GroupBy(q => q.ChapterId!.Value)
            .Select(g => new { ChapterId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ChapterId, x => x.Count, cancellationToken);
    }
}
