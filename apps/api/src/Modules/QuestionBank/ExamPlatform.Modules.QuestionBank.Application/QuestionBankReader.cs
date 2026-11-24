using ExamPlatform.Modules.QuestionBank.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.Modules.QuestionBank.Domain;

namespace ExamPlatform.Modules.QuestionBank.Application;

/// <summary>The <see cref="IQuestionBank"/> other modules read the bank through.</summary>
/// <remarks>
/// Content is read at the version asked for, and a stored version never changes (FR-7), so what is read for a question at a version can be
/// kept and handed out again without asking the database or decrypting it a second time. Where a question is filed and whether it can be used
/// do change without a new version, so those are read every time, from plain columns.
/// </remarks>
public sealed class QuestionBankReader(
    IQuestionRepository repository, IBookRepository books, QuestionApprovalPolicy? approval = null, IQuestionVersionCache? versionCache = null) : IQuestionBank
{
    private readonly QuestionApprovalPolicy _approval = approval ?? new QuestionApprovalPolicy(RequireApproval: false);
    private readonly QuestionVersionResolver _resolver = new(repository, versionCache ?? NoQuestionVersionCache.Instance);

    /// <inheritdoc />
    public async Task<IReadOnlyList<QuestionSnapshot>> GetAsync(IReadOnlyCollection<Guid> questionIds, CancellationToken cancellationToken)
    {
        if (questionIds.Count == 0)
            return [];

        var questions = await repository.GetManyAsync(questionIds, cancellationToken);
        var current = await repository.GetCurrentVersionNumbersAsync(questionIds, cancellationToken);
        var chapters = await ChaptersOfAsync(questions.Select(q => q.ChapterId), cancellationToken);

        return questions.Select(q => Current(q, current, chapters, _approval)).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<QuestionSnapshot>> GetVersionsAsync(IReadOnlyCollection<QuestionVersionRef> versions, CancellationToken cancellationToken)
    {
        if (versions.Count == 0)
            return [];

        // One reference per question: when a question is named twice, the first says which version is wanted.
        var wanted = versions.GroupBy(v => v.QuestionId).Select(g => g.First()).ToList();

        // Plain columns only, so nothing is decrypted to find where a question is filed, whether it can be used and which version is in force.
        var facts = (await repository.GetFactsAsync(wanted.Select(v => v.QuestionId).ToList(), cancellationToken)).ToDictionary(f => f.Id);
        var chapters = await ChaptersOfAsync(facts.Values.Select(f => f.ChapterId), cancellationToken);

        var resolved = await _resolver.ResolveAsync(wanted, facts, cancellationToken);

        var now = resolved.AsItIsNow.Count == 0
            ? new Dictionary<Guid, QuestionSnapshot>()
            : await ReadAsItIsNowAsync(resolved.AsItIsNow, facts, chapters, cancellationToken);

        var snapshots = new List<QuestionSnapshot>(resolved.Found.Count + now.Count);
        foreach (var reference in wanted)
        {
            if (resolved.Found.TryGetValue(reference.QuestionId, out var read))
            {
                var fact = facts[reference.QuestionId];
                var c = read.Content;
                snapshots.Add(new QuestionSnapshot(
                    fact.Id, c.Text, c.Options, fact.ChapterId, BookOf(fact.ChapterId, chapters), c.AllowsMultiple, read.Number,
                    // Whether a question can be used is about the question as it is now, so it is only said of the version in force.
                    read.Number == fact.CurrentVersion ? _approval.UnusableReason(fact.Status) : null,
                    c.IsTextAnswer, c.AcceptedAnswers, c.Explanation, c.MatchRule));
            }
            else if (now.TryGetValue(reference.QuestionId, out var snapshot))
            {
                snapshots.Add(snapshot);
            }
        }

        return snapshots;
    }

    private async Task<Dictionary<Guid, QuestionSnapshot>> ReadAsItIsNowAsync(
        IReadOnlyList<Guid> ids, IReadOnlyDictionary<Guid, QuestionFacts> facts, IReadOnlyDictionary<Guid, ChapterRef> chapters, CancellationToken cancellationToken)
    {
        var current = facts.Where(f => f.Value.CurrentVersion is not null).ToDictionary(f => f.Key, f => f.Value.CurrentVersion!.Value);
        var questions = await repository.GetManyAsync(ids, cancellationToken);
        return questions.ToDictionary(q => q.Id, q => Current(q, current, chapters, _approval));
    }

    private async Task<IReadOnlyDictionary<Guid, ChapterRef>> ChaptersOfAsync(IEnumerable<Guid?> chapterIds, CancellationToken cancellationToken) =>
        await books.GetChapterRefsAsync(chapterIds.Where(c => c is not null).Select(c => c!.Value).Distinct().ToList(), cancellationToken);

    private static Guid? BookOf(Guid? chapterId, IReadOnlyDictionary<Guid, ChapterRef> chapters) =>
        chapterId is { } id && chapters.TryGetValue(id, out var filedUnder) ? filedUnder.BookId : null;

    private static Guid? BookOf(Question q, IReadOnlyDictionary<Guid, ChapterRef> chapters) => BookOf(q.ChapterId, chapters);

    private static QuestionSnapshot Current(
        Question q, IReadOnlyDictionary<Guid, int> current, IReadOnlyDictionary<Guid, ChapterRef> chapters, QuestionApprovalPolicy approval) =>
        new(
            q.Id,
            q.Text,
            q.Options.OrderBy(o => o.Order).Select(o => new QuestionOptionSnapshot(o.Id, o.Text, o.IsCorrect, o.IsPinned)).ToList(),
            q.ChapterId,
            BookOf(q, chapters),
            q.AllowsMultiple,
            current.GetValueOrDefault(q.Id, 1),
            approval.UnusableReason(q.Status),
            q.IsTextAnswer,
            q.AcceptedAnswers,
            q.Explanation,
            q.MatchRule);

    /// <inheritdoc />
    public async Task<IReadOnlyList<QuestionTranslationSnapshot>> GetTranslationsAsync(
        IReadOnlyCollection<Guid> questionIds, IReadOnlyList<string> languages, CancellationToken cancellationToken)
    {
        var wanted = languages.Where(l => QuestionLanguage.Supported.Contains(l)).Distinct().ToList();
        if (questionIds.Count == 0 || wanted.Count == 0)
            return [];

        // Only the question's group and language are needed to find its translation, so only those are read: loading the questions themselves
        // would decrypt the whole paper once more, for a candidate whose language is usually the one the exam was written in.
        var sources = await repository.GetTranslationSourcesAsync(questionIds, cancellationToken);

        // A question already in the language wanted most is shown as it is, whatever else the bank holds for it, so only the others need
        // their translations looked for; when there are none, there is nothing more to read.
        var toTranslate = sources.Where(s => s.Language != wanted[0]).ToList();
        if (toTranslate.Count == 0)
            return [];

        var candidates = await repository.ListTranslationCandidatesAsync(
            toTranslate.Select(s => s.TranslationGroupId).Distinct().ToList(), wanted, _approval.UsableStatuses, cancellationToken);

        var found = new List<QuestionTranslationSnapshot>();
        foreach (var source in toTranslate)
        {
            foreach (var language in wanted)
            {
                // The question is already in a language the caller prefers to any further down their list, so it is shown as it is.
                if (source.Language == language)
                    break;

                var translation = candidates.FirstOrDefault(c => c.TranslationGroupId == source.TranslationGroupId && c.Language == language && c.Id != source.Id);
                if (translation is null)
                    continue;

                found.Add(new QuestionTranslationSnapshot(
                    source.Id, language, translation.Text, translation.Options.OrderBy(o => o.Order).Select(o => o.Text).ToList(), translation.Explanation));
                break;
            }
        }

        return found;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FoundQuestion>> FindAsync(QuestionCriteria criteria, CancellationToken cancellationToken)
    {
        var filter = new QuestionFilter(
            criteria.BookId, criteria.ChapterId, UnfiledOnly: false,
            QuestionDifficultyText.Parse(criteria.Difficulty), Question.NormalizeTopic(criteria.Topic),
            Search: null, criteria.ChapterIds, _approval.UsableStatuses);

        var found = await repository.FindPlacementsAsync(filter, IQuestionBank.MaxFound, cancellationToken);
        var chapters = await books.GetChapterRefsAsync(
            found.Where(q => q.ChapterId is not null).Select(q => q.ChapterId!.Value).Distinct().ToList(), cancellationToken);

        return found
            .Select(q => new FoundQuestion(
                q.Id,
                q.ChapterId,
                q.ChapterId is { } chapterId && chapters.TryGetValue(chapterId, out var filedUnder) ? filedUnder.BookId : null))
            .ToList();
    }
}
