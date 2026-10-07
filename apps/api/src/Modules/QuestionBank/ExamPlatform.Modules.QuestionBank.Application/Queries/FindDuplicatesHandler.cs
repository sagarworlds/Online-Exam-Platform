using ExamPlatform.Modules.QuestionBank.Application.Dtos;
using ExamPlatform.Modules.QuestionBank.Application.Ports;

namespace ExamPlatform.Modules.QuestionBank.Application.Queries;

/// <summary>Asks which questions in the bank already repeat one an author is about to add (FR-9).</summary>
public sealed class FindDuplicatesHandler(QuestionDuplicateFinder finder, IRichTextSanitizer sanitizer)
{
    /// <summary>Looks for questions with the same wording, and says which of them also have the same options.</summary>
    /// <param name="text">The question text as the editor produced it (HTML); only its readable words are compared.</param>
    /// <param name="options">The texts of the options.</param>
    /// <param name="excludeQuestionId">A question being edited, which is not a duplicate of itself.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matches, those with the same options first; empty when nothing repeats it, or there is nothing readable to compare.</returns>
    public async Task<IReadOnlyList<DuplicateQuestionDto>> HandleAsync(
        string? text, IReadOnlyList<string?>? options, Guid? excludeQuestionId, CancellationToken cancellationToken)
    {
        var plain = sanitizer.Sanitize(text).PlainText;
        var matches = await finder.FindAsync(plain, (options ?? []).Select(o => o ?? string.Empty).ToList(), excludeQuestionId, cancellationToken);

        return matches.Select(m => new DuplicateQuestionDto(
            m.Question.Id,
            m.Question.SearchText.Length <= DuplicateQuestionDto.PreviewLength ? m.Question.SearchText : m.Question.SearchText[..DuplicateQuestionDto.PreviewLength] + "…",
            m.SameOptions,
            QuestionStatusText.Format(m.Question.Status),
            m.Question.ChapterId)).ToList();
    }
}
