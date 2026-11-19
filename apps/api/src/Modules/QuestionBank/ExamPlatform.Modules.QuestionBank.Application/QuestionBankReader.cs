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
    private readonly IQuestionVersionCache _versions = versionCache ?? NoQuestionVersionCache.Instance;

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

        var found = new Dictionary<Guid, (int Number, QuestionVersionContent Content)>();
        var missed = new List<(Guid QuestionId, int VersionNumber)>();
        var asItIsNow = new List<Guid>();
        foreach (var reference in wanted)
        {
            if (!facts.TryGetValue(reference.QuestionId, out var fact))
                continue;

            // No version was ever stored (a question from before versions were kept), or the one asked for never was: it reads as it is now.
            var number = reference.VersionNumber ?? fact.CurrentVersion;
            if (fact.CurrentVersion is null || number is null || number < 1 || number > fact.CurrentVersion)
            {
                asItIsNow.Add(reference.QuestionId);
                continue;
            }

            if (_versions.TryGet(reference.QuestionId, number.Value, out var kept))
                found[reference.QuestionId] = (number.Value, kept);
            else
                missed.Add((reference.QuestionId, number.Value));
        }

        if (missed.Count > 0)
        {
            // Read from the stored version, which cannot change under the read, and not from the question, which an edit could change between
            // learning the number and reading the text: what is kept under a number must be the content of that number.
            var stored = (await repository.GetVersionsAsync(missed, cancellationToken)).ToDictionary(v => (v.QuestionId, v.VersionNumber));
            foreach (var (questionId, number) in missed)
            {
                if (!stored.TryGetValue((questionId, number), out var version))
                {
                    asItIsNow.Add(questionId);
                    continue;
                }

                var content = ContentOf(version);
                _versions.Set(questionId, number, content);
                found[questionId] = (number, content);
            }
        }

        var now = asItIsNow.Count == 0
            ? new Dictionary<Guid, QuestionSnapshot>()
            : await ReadAsItIsNowAsync(asItIsNow, facts, chapters, cancellationToken);

        var snapshots = new List<QuestionSnapshot>(found.Count + now.Count);
        foreach (var reference in wanted)
        {
            if (found.TryGetValue(reference.QuestionId, out var read))
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
        List<Guid> ids, IReadOnlyDictionary<Guid, QuestionFacts> facts, IReadOnlyDictionary<Guid, ChapterRef> chapters, CancellationToken cancellationToken)
    {
        var current = facts.Where(f => f.Value.CurrentVersion is not null).ToDictionary(f => f.Key, f => f.Value.CurrentVersion!.Value);
        var questions = await repository.GetManyAsync(ids, cancellationToken);
        return questions.ToDictionary(q => q.Id, q => Current(q, current, chapters, _approval));
    }

    // The lists are handed to every reader of the cached content, so they are read-only wrappers: a list or an array that a caller could cast
    // back and sort or edit in place would change the content for everyone after it.
    private static QuestionVersionContent ContentOf(QuestionVersion v) =>
        new(
            v.Text,
            v.Options.OrderBy(o => o.Order).Select(o => new QuestionOptionSnapshot(o.OptionId, o.Text, o.IsCorrect, o.IsPinned)).ToList().AsReadOnly(),
            v.AllowsMultiple,
            v.IsTextAnswer,
            Array.AsReadOnly(v.AcceptedAnswers),
            v.Explanation,
            v.MatchRule);

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
