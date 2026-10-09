using ExamPlatform.Modules.QuestionBank.Domain;
using ExamPlatform.Modules.QuestionBank.Domain.Exceptions;

namespace ExamPlatform.Modules.QuestionBank.Application;

/// <summary>Converts a difficulty between the text the API speaks ("easy") and the domain's enum, so no caller parses it its own way.</summary>
public static class QuestionDifficultyText
{
    /// <summary>Reads a difficulty from request text, ignoring case.</summary>
    /// <param name="text">The text; null or blank means no difficulty.</param>
    /// <returns>The difficulty, or null when none was given.</returns>
    /// <exception cref="InvalidQuestionError">The text is not easy, medium or hard.</exception>
    public static QuestionDifficulty? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        // IsDefined stops a number such as "7" from parsing into a value the enum does not have.
        if (Enum.TryParse<QuestionDifficulty>(text.Trim(), ignoreCase: true, out var level) && Enum.IsDefined(level))
            return level;

        throw new InvalidQuestionError("The difficulty must be easy, medium or hard.");
    }

    /// <summary>The text a response carries for a difficulty.</summary>
    /// <param name="difficulty">The difficulty, or null.</param>
    /// <returns>"easy", "medium" or "hard", or null for none.</returns>
    public static string? Format(QuestionDifficulty? difficulty) => difficulty?.ToString().ToLowerInvariant();
}
