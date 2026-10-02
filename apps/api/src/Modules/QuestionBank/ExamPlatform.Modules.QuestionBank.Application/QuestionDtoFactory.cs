using ExamPlatform.Modules.QuestionBank.Application.Dtos;
using ExamPlatform.Modules.QuestionBank.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Domain;

namespace ExamPlatform.Modules.QuestionBank.Application;

/// <summary>Builds the DTO of one question, with the book and chapter it is filed under, so every single-question handler reports it alike.</summary>
public sealed class QuestionDtoFactory(IBookRepository books)
{
    /// <summary>Maps the question and looks up where it is filed.</summary>
    /// <param name="question">The question to report.</param>
    /// <param name="usage">Where the question is in use.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<QuestionDto> CreateAsync(Question question, QuestionUsageDto usage, CancellationToken cancellationToken)
    {
        ChapterRef? filedUnder = null;
        if (question.ChapterId is { } chapterId)
            filedUnder = (await books.GetChapterRefsAsync([chapterId], cancellationToken)).GetValueOrDefault(chapterId);

        return question.ToDto(filedUnder, usage);
    }
}
