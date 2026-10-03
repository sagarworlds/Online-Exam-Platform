using ExamPlatform.Modules.QuestionBank.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.Modules.QuestionBank.Domain;

namespace ExamPlatform.Modules.QuestionBank.Application;

/// <summary>The <see cref="IQuestionBank"/> other modules read the bank through.</summary>
public sealed class QuestionBankReader(IQuestionRepository repository, IBookRepository books) : IQuestionBank
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<QuestionSnapshot>> GetAsync(IReadOnlyCollection<Guid> questionIds, CancellationToken cancellationToken)
    {
        if (questionIds.Count == 0)
            return [];

        var questions = await repository.GetManyAsync(questionIds, cancellationToken);
        var chapters = await books.GetChapterRefsAsync(
            questions.Where(q => q.ChapterId is not null).Select(q => q.ChapterId!.Value).Distinct().ToList(), cancellationToken);

        return questions
            .Select(q => new QuestionSnapshot(
                q.Id,
                q.Text,
                q.Options.OrderBy(o => o.Order).Select(o => new QuestionOptionSnapshot(o.Id, o.Text, o.IsCorrect, o.IsPinned)).ToList(),
                q.ChapterId,
                q.ChapterId is { } chapterId && chapters.TryGetValue(chapterId, out var filedUnder) ? filedUnder.BookId : null))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FoundQuestion>> FindAsync(QuestionCriteria criteria, CancellationToken cancellationToken)
    {
        var filter = new QuestionFilter(
            criteria.BookId, criteria.ChapterId, UnfiledOnly: false,
            QuestionDifficultyText.Parse(criteria.Difficulty), Question.NormalizeTopic(criteria.Topic));

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
