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

    // Versions included here (and only here): this is the load used to revise a question or correct its key, both of
    // which append a new one, and EF only tracks a child collection's inserts when the collection is loaded.
    /// <inheritdoc />
    public Task<Question?> GetByIdAsync(Guid questionId, CancellationToken cancellationToken) =>
        context.Questions.Include(q => q.Options).Include(q => q.Versions).FirstOrDefaultAsync(q => q.Id == questionId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Question>> GetManyAsync(IReadOnlyCollection<Guid> questionIds, CancellationToken cancellationToken) =>
        await context.Questions.AsNoTracking().Include(q => q.Options)
            .Where(q => questionIds.Contains(q.Id))
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public void AddReviewEntry(QuestionReviewEntry entry) => context.QuestionReviewEntries.Add(entry);

    /// <inheritdoc />
    public async Task<IReadOnlyList<QuestionReviewEntry>> ListReviewEntriesAsync(Guid questionId, CancellationToken cancellationToken) =>
        await context.QuestionReviewEntries.AsNoTracking()
            .Where(e => e.QuestionId == questionId)
            .OrderBy(e => e.CreatedAtUtc).ThenBy(e => e.Id)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, int>> GetCurrentVersionNumbersAsync(IReadOnlyCollection<Guid> questionIds, CancellationToken cancellationToken) =>
        await context.QuestionVersions.AsNoTracking()
            .Where(v => questionIds.Contains(v.QuestionId))
            .GroupBy(v => v.QuestionId)
            .Select(g => new { QuestionId = g.Key, Number = g.Max(v => v.VersionNumber) })
            .ToDictionaryAsync(x => x.QuestionId, x => x.Number, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<QuestionVersion>> GetVersionsAsync(IReadOnlyCollection<(Guid QuestionId, int VersionNumber)> versions, CancellationToken cancellationToken)
    {
        var ids = versions.Select(v => v.QuestionId).Distinct().ToList();
        var numbers = versions.Select(v => v.VersionNumber).Distinct().ToList();
        // The two filters over-select a little (a number wanted of one question matches another's); the exact pairs are kept in memory.
        var found = await context.QuestionVersions.AsNoTracking()
            .Where(v => ids.Contains(v.QuestionId) && numbers.Contains(v.VersionNumber))
            .ToListAsync(cancellationToken);
        var wanted = versions.ToHashSet();
        return found.Where(v => wanted.Contains((v.QuestionId, v.VersionNumber))).ToList();
    }

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

    // What the author typed is text to find, not a pattern: a "%" or "_" in it must match itself, so the wildcards are escaped.
    private static string EscapeLike(string text) => text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private IQueryable<Question> Matching(IQueryable<Question> query, QuestionFilter filter)
    {
        if (filter.UnfiledOnly)
            query = query.Where(q => q.ChapterId == null);
        if (filter.ChapterId is { } chapterId)
            query = query.Where(q => q.ChapterId == chapterId);
        if (filter.ChapterIds is { Count: > 0 } chapterIds)
            query = query.Where(q => q.ChapterId != null && chapterIds.Contains(q.ChapterId.Value));
        if (filter.BookId is { } bookId)
            query = query.Where(q => context.Chapters.Any(c => c.Id == q.ChapterId && c.BookId == bookId));
        if (filter.Difficulty is { } difficulty)
            query = query.Where(q => q.Difficulty == difficulty);
        if (filter.Language is { Length: > 0 } language)
            query = query.Where(q => q.Language == language);
        if (filter.Statuses is { Count: > 0 } statuses)
            query = query.Where(q => statuses.Contains(q.Status));
        if (filter.Topic is { Length: > 0 } topic)
            query = query.Where(q => q.Topics.Contains(topic));
        if (filter.Search?.Trim() is { Length: > 0 } search)
        {
            var pattern = $"%{EscapeLike(search)}%";
            query = query.Where(q => EF.Functions.ILike(q.SearchText, pattern, "\\")
                || q.Options.Any(o => EF.Functions.ILike(o.Text, pattern, "\\")));
        }

        return query;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Question>> FindByTextKeyAsync(string textKey, Guid? excludeQuestionId, int take, CancellationToken cancellationToken) =>
        string.IsNullOrEmpty(textKey)
            ? []
            : await context.Questions.AsNoTracking().Include(q => q.Options)
                .Where(q => q.TextKey == textKey && q.Id != excludeQuestionId)
                .OrderBy(q => q.CreatedAtUtc).ThenBy(q => q.Id).Take(take).ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Question>> ListTranslationGroupAsync(Guid translationGroupId, CancellationToken cancellationToken) =>
        await context.Questions.AsNoTracking()
            .Where(q => q.TranslationGroupId == translationGroupId)
            .OrderBy(q => q.CreatedAtUtc).ThenBy(q => q.Id)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Question>> ListTranslationCandidatesAsync(
        IReadOnlyCollection<Guid> translationGroupIds, IReadOnlyCollection<string> languages, IReadOnlyCollection<QuestionStatus> statuses, CancellationToken cancellationToken) =>
        await context.Questions.AsNoTracking().Include(q => q.Options)
            .Where(q => translationGroupIds.Contains(q.TranslationGroupId) && languages.Contains(q.Language) && statuses.Contains(q.Status))
            .ToListAsync(cancellationToken);

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
