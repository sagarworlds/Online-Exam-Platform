using ExamPlatform.Modules.QuestionBank.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Contracts;

namespace ExamPlatform.Modules.QuestionBank.Application;

/// <summary>The <see cref="IQuestionSubjects"/> the leaderboards read a question's subject through (FR-35).</summary>
public sealed class QuestionSubjects(IQuestionRepository repository, IBookRepository books) : IQuestionSubjects
{
    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, string>> GetSubjectsAsync(IReadOnlyCollection<Guid> questionIds, CancellationToken cancellationToken)
    {
        var subjects = new Dictionary<Guid, string>();
        if (questionIds.Count == 0)
            return subjects;

        var questions = await repository.GetManyAsync(questionIds, cancellationToken);
        var chapterIds = questions.Where(q => q.ChapterId is not null).Select(q => q.ChapterId!.Value).Distinct().ToList();
        var chapters = await books.GetChapterRefsAsync(chapterIds, cancellationToken);

        foreach (var question in questions)
        {
            // A question is ranked by the subject of the book it is filed under; one with no chapter, or whose book names no subject, has none.
            if (question.ChapterId is { } chapterId
                && chapters.TryGetValue(chapterId, out var filedUnder)
                && !string.IsNullOrWhiteSpace(filedUnder.BookSubject))
            {
                subjects[question.Id] = filedUnder.BookSubject.Trim();
            }
        }

        return subjects;
    }
}
