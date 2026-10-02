using ExamPlatform.Modules.QuestionBank.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Contracts;

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
                q.Options.OrderBy(o => o.Order).Select(o => new QuestionOptionSnapshot(o.Id, o.Text, o.IsCorrect)).ToList(),
                q.ChapterId,
                q.ChapterId is { } chapterId && chapters.TryGetValue(chapterId, out var filedUnder) ? filedUnder.BookId : null))
            .ToList();
    }
}
