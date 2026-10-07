namespace ExamPlatform.Modules.QuestionBank.Application.Dtos;

/// <summary>One question in a group of translations (FR-10).</summary>
/// <param name="Id">The question's id.</param>
/// <param name="Language">The language code it is written in, such as "en", "hi" or "mr".</param>
/// <param name="Preview">The start of its wording as plain text.</param>
/// <param name="Status">Where it is in the review workflow (FR-8); each translation is reviewed on its own.</param>
public sealed record QuestionTranslationDto(Guid Id, string Language, string Preview, string Status)
{
    /// <summary>How many characters of the wording a preview carries before it is cut short.</summary>
    public const int PreviewLength = 160;
}
