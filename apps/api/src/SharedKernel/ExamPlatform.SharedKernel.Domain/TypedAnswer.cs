namespace ExamPlatform.SharedKernel.Domain;

/// <summary>
/// The rule that decides whether a typed answer is right (a text question, FR-5): the author lists the accepted answers, and a
/// candidate's answer counts when it matches one of them. Shared by the question bank, which refuses two accepted answers that
/// mean the same thing, and by the exam runtime, which marks the answer, so the two can never disagree about what "the same" is.
/// </summary>
public static class TypedAnswer
{
    /// <summary>The longest answer a candidate may type, after trimming. Answers are short; this bounds what a candidate can store.</summary>
    public const int MaxLength = 1000;

    /// <summary>
    /// The form two answers are compared in: trimmed, inner whitespace collapsed to one space, lower case. "  Paris " and "paris"
    /// are the same answer, so a candidate is not marked wrong for a stray space or a capital letter.
    /// </summary>
    /// <param name="text">The answer as typed or as the author wrote it; null counts as blank.</param>
    /// <returns>The comparable form, which is empty for a blank answer.</returns>
    public static string Normalize(string? text) =>
        string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    /// <summary>Whether a typed answer is one of the accepted answers, comparing in the form <see cref="Normalize"/> gives.</summary>
    /// <param name="typed">What the candidate typed; a blank answer matches nothing.</param>
    /// <param name="accepted">The answers the author accepted.</param>
    /// <returns><see langword="true"/> when the typed answer is one of them.</returns>
    public static bool Matches(string? typed, IEnumerable<string> accepted)
    {
        var answer = Normalize(typed);
        return answer.Length > 0 && accepted.Any(a => Normalize(a) == answer);
    }
}
