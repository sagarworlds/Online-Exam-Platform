using ExamPlatform.Modules.QuestionBank.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.Modules.QuestionBank.Domain;

namespace ExamPlatform.Modules.QuestionBank.Application;

/// <summary>The <see cref="IQuestionBank"/> other modules read the bank through.</summary>
public sealed class QuestionBankReader(IQuestionRepository repository, IBookRepository books, QuestionApprovalPolicy? approval = null) : IQuestionBank
{
    private readonly QuestionApprovalPolicy _approval = approval ?? new QuestionApprovalPolicy(RequireApproval: false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<QuestionSnapshot>> GetAsync(IReadOnlyCollection<Guid> questionIds, CancellationToken cancellationToken)
    {
        if (questionIds.Count == 0)
            return [];

        var questions = await repository.GetManyAsync(questionIds, cancellationToken);
        var current = await repository.GetCurrentVersionNumbersAsync(questionIds, cancellationToken);
        var chapters = await ChaptersOfAsync(questions, cancellationToken);

        return questions.Select(q => Current(q, current, chapters, _approval)).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<QuestionSnapshot>> GetVersionsAsync(IReadOnlyCollection<QuestionVersionRef> versions, CancellationToken cancellationToken)
    {
        if (versions.Count == 0)
            return [];

        var ids = versions.Select(v => v.QuestionId).Distinct().ToList();
        var questions = await repository.GetManyAsync(ids, cancellationToken);
        var current = await repository.GetCurrentVersionNumbersAsync(ids, cancellationToken);
        var chapters = await ChaptersOfAsync(questions, cancellationToken);

        // Only the versions that are not the current one need reading: the current content is already in hand.
        var older = versions
            .Where(v => v.VersionNumber is { } n && n != current.GetValueOrDefault(v.QuestionId, 1))
            .Select(v => (v.QuestionId, v.VersionNumber!.Value))
            .Distinct().ToList();
        var stored = older.Count == 0
            ? new Dictionary<(Guid, int), QuestionVersion>()
            : (await repository.GetVersionsAsync(older, cancellationToken)).ToDictionary(v => (v.QuestionId, v.VersionNumber));

        return questions.Select(q =>
        {
            var wanted = versions.First(v => v.QuestionId == q.Id).VersionNumber;
            // A version that was never stored (a question from before versions were kept) reads as it is now.
            return wanted is { } n && stored.TryGetValue((q.Id, n), out var version)
                ? AsOf(q, version, chapters)
                : Current(q, current, chapters, _approval);
        }).ToList();
    }

    private async Task<IReadOnlyDictionary<Guid, ChapterRef>> ChaptersOfAsync(IReadOnlyList<Question> questions, CancellationToken cancellationToken) =>
        await books.GetChapterRefsAsync(
            questions.Where(q => q.ChapterId is not null).Select(q => q.ChapterId!.Value).Distinct().ToList(), cancellationToken);

    private static Guid? BookOf(Question q, IReadOnlyDictionary<Guid, ChapterRef> chapters) =>
        q.ChapterId is { } chapterId && chapters.TryGetValue(chapterId, out var filedUnder) ? filedUnder.BookId : null;

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
            q.AcceptedAnswers);

    private static QuestionSnapshot AsOf(Question q, QuestionVersion v, IReadOnlyDictionary<Guid, ChapterRef> chapters) =>
        new(
            q.Id,
            v.Text,
            v.Options.OrderBy(o => o.Order).Select(o => new QuestionOptionSnapshot(o.OptionId, o.Text, o.IsCorrect, o.IsPinned)).ToList(),
            q.ChapterId,
            BookOf(q, chapters),
            v.AllowsMultiple,
            v.VersionNumber,
            IsTextAnswer: v.IsTextAnswer,
            AcceptedAnswers: v.AcceptedAnswers);

    /// <inheritdoc />
    public async Task<IReadOnlyList<QuestionTranslationSnapshot>> GetTranslationsAsync(
        IReadOnlyCollection<Guid> questionIds, IReadOnlyList<string> languages, CancellationToken cancellationToken)
    {
        var wanted = languages.Where(l => QuestionLanguage.Supported.Contains(l)).Distinct().ToList();
        if (questionIds.Count == 0 || wanted.Count == 0)
            return [];

        var sources = await repository.GetManyAsync(questionIds, cancellationToken);
        var candidates = await repository.ListTranslationCandidatesAsync(
            sources.Select(s => s.TranslationGroupId).Distinct().ToList(), wanted, _approval.UsableStatuses, cancellationToken);

        var found = new List<QuestionTranslationSnapshot>();
        foreach (var source in sources)
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
                    source.Id, language, translation.Text, translation.Options.OrderBy(o => o.Order).Select(o => o.Text).ToList()));
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
