namespace ExamPlatform.Modules.QuestionBank.Contracts;

/// <summary>A question's wording in another language (FR-51), to show a candidate in place of the question's own.</summary>
/// <param name="QuestionId">The question being shown, not the translation's own id: whoever asked reads the translation as that question.</param>
/// <param name="Language">The language the translation is written in, such as "hi".</param>
/// <param name="Text">The translated question text, as sanitized HTML.</param>
/// <param name="OptionTexts">
/// The translated options in display order, one for each option of the question as it stands now. A translation is made with as many
/// options as its source had, and the caller pairs them with the options it is showing by position, so it must check the counts match.
/// </param>
/// <param name="Explanation">
/// The translation's own explanation, in its language, or null when it has none. It is not the source's explanation: a candidate shown a
/// translated question sees the translated explanation or none, never the source language's explanation under translated wording.
/// </param>
public sealed record QuestionTranslationSnapshot(Guid QuestionId, string Language, string Text, IReadOnlyList<string> OptionTexts, string? Explanation = null);
