using ExamPlatform.Modules.QuestionBank.Domain;

namespace ExamPlatform.Modules.QuestionBank.Application;

/// <summary>
/// The author's text search over the bank (FR-8). Question content is stored encrypted (#57), so the database cannot match it; the search
/// runs here, over the decrypted text, and matches what the author typed as a plain substring, ignoring case.
/// </summary>
public static class QuestionTextSearch
{
    /// <summary>The search term the author typed, trimmed, or null when there is nothing to search for.</summary>
    /// <param name="search">The term as typed.</param>
    public static string? TermOf(string? search) => search?.Trim() is { Length: > 0 } term ? term : null;

    /// <summary>Whether a question's stem or one of its options contains the term, ignoring case.</summary>
    /// <param name="term">The term, already trimmed (see <see cref="TermOf"/>); a question matches every search when no term is set.</param>
    /// <param name="question">The question, with its options loaded.</param>
    public static bool Matches(string term, Question question) =>
        question.SearchText.Contains(term, StringComparison.OrdinalIgnoreCase)
        || question.Options.Any(option => option.Text.Contains(term, StringComparison.OrdinalIgnoreCase));
}
