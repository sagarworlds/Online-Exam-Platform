using ExamPlatform.Modules.QuestionBank.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Domain;
using ExamPlatform.Modules.QuestionBank.Domain.Exceptions;

namespace ExamPlatform.Modules.QuestionBank.Application;

/// <summary>
/// The rules about question text that need the sanitizer, in one place so creating and editing a question can never
/// disagree about what an acceptable question looks like. (The rules that need no sanitizer live in <see cref="Question"/>.)
/// </summary>
internal static class QuestionText
{
    /// <summary>Cleans the text an author sent and checks everything that is about what survives the cleaning.</summary>
    /// <param name="sanitizer">The rich-text sanitizer.</param>
    /// <param name="text">The text as the author's editor produced it (HTML).</param>
    /// <returns>The sanitized text: its HTML is the only form that may be stored, and its plain text is what searching reads.</returns>
    /// <exception cref="InvalidQuestionError">A picture was rejected, nothing visible is left, or the text is too long or has too many images.</exception>
    public static SanitizedRichText Clean(IRichTextSanitizer sanitizer, string? text)
    {
        // Sanitizing comes first: every length rule below is about what survives the cleaning, not what was sent.
        var cleaned = sanitizer.Sanitize(text);

        if (cleaned.RejectedImageCount > 0)
            throw new InvalidQuestionError(
                "A picture could not be used. Add pictures with the image button: PNG, JPEG, GIF or WebP, " +
                $"at most {Question.MaxImageBytes / 1024} KB each.");
        if (!cleaned.HasContent)
            throw new InvalidQuestionError("The question text is required.");
        if (cleaned.PlainText.Length > Question.MaxVisibleTextLength)
            throw new InvalidQuestionError($"The question text must be at most {Question.MaxVisibleTextLength} characters.");
        if (cleaned.ImageCount > Question.MaxImages)
            throw new InvalidQuestionError($"A question can have at most {Question.MaxImages} images.");

        return cleaned;
    }
}
