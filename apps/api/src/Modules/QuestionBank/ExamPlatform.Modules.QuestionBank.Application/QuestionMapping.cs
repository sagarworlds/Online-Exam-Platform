using ExamPlatform.Modules.QuestionBank.Application.Dtos;
using ExamPlatform.Modules.QuestionBank.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Domain;

namespace ExamPlatform.Modules.QuestionBank.Application;

/// <summary>Maps <see cref="Question"/> to its DTO, so every handler reports a question the same way.</summary>
internal static class QuestionMapping
{
    /// <summary>Maps a question and its options, in display order (the database returns them in no particular order).</summary>
    /// <param name="question">The question to map.</param>
    /// <param name="filedUnder">Where the question is filed, or null when it is not filed anywhere.</param>
    /// <param name="usage">Where the question is in use; null means nothing uses it, which is true of a question just created.</param>
    public static QuestionDto ToDto(this Question question, ChapterRef? filedUnder = null, QuestionUsageDto? usage = null) =>
        new(
            question.Id,
            question.Text,
            question.Options.OrderBy(o => o.Order).Select(o => new QuestionOptionDto(o.Id, o.Text, o.IsCorrect, o.IsPinned)).ToList(),
            question.CreatedBy,
            question.CreatedAtUtc,
            filedUnder?.ChapterId,
            filedUnder?.ChapterTitle,
            filedUnder?.BookId,
            filedUnder?.BookName,
            usage ?? QuestionUsageDto.Unused);
}
