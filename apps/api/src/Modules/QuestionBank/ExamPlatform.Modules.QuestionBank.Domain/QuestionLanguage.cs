using ExamPlatform.Modules.QuestionBank.Domain.Exceptions;

namespace ExamPlatform.Modules.QuestionBank.Domain;

/// <summary>
/// The languages a question can be written in (FR-10), as the short lower-case codes stored and sent over the API. English,
/// Hindi and Marathi come first; another language is added by adding its code to <see cref="Supported"/>.
/// </summary>
public static class QuestionLanguage
{
    /// <summary>English, the language of every question written before languages existed.</summary>
    public const string English = "en";

    /// <summary>Hindi.</summary>
    public const string Hindi = "hi";

    /// <summary>Marathi.</summary>
    public const string Marathi = "mr";

    /// <summary>The longest language code a question may carry, which bounds the stored column.</summary>
    public const int MaxLength = 8;

    /// <summary>Every language a question may be written in, in the order they are offered.</summary>
    public static IReadOnlyList<string> Supported { get; } = [English, Hindi, Marathi];

    /// <summary>Reads a language from request text.</summary>
    /// <param name="language">The code, such as "hi"; case and surrounding spaces do not matter. Null or blank means <see cref="English"/>.</param>
    /// <returns>The code in the form it is stored.</returns>
    /// <exception cref="InvalidQuestionError">The language is not one of <see cref="Supported"/>.</exception>
    public static string Parse(string? language)
    {
        var code = language?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(code))
            return English;

        return Supported.Contains(code) ? code : throw new InvalidQuestionError($"The language must be one of: {string.Join(", ", Supported)}.");
    }
}
