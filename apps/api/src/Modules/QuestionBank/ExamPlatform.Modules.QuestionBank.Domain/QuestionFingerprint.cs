using System.Security.Cryptography;
using System.Text;

namespace ExamPlatform.Modules.QuestionBank.Domain;

/// <summary>
/// What two questions must share to be called the same question (FR-9): the words of the stem and of the options, with case,
/// spacing and punctuation ignored, so "What is 2 + 2?" and "what is  2+2" are one question.
/// </summary>
/// <remarks>
/// Stored as a hash so it can be indexed whatever the length of the text. Existing questions got theirs from a SQL expression that
/// does the same thing for plain letters and digits; an accented or unusual character may normalise differently there, which can only
/// make an old question be missed as a duplicate, never wrongly matched, and an edit recomputes it exactly.
/// </remarks>
public static class QuestionFingerprint
{
    /// <summary>Keeps letters and digits, lower-cased; everything else is dropped.</summary>
    /// <param name="text">Plain text (markup already removed); null counts as empty.</param>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c))
                builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }

    /// <summary>The key a question's stem is looked up by.</summary>
    /// <param name="plainText">The question text with all markup removed.</param>
    /// <returns>A hash of the normalised text, or empty when nothing readable is left, such as a question that is only a picture; an empty key never matches anything.</returns>
    public static string KeyOf(string? plainText)
    {
        var normalized = Normalize(plainText);
        return normalized.Length == 0
            ? string.Empty
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
    }

    /// <summary>The options as a set, so the same options in another order, or with other spacing, are the same options.</summary>
    /// <param name="optionTexts">The texts of the options.</param>
    public static string OptionsKeyOf(IEnumerable<string> optionTexts) =>
        string.Join('\u001f', optionTexts.Select(Normalize).Order(StringComparer.Ordinal));
}
