using System.Text;

namespace ExamPlatform.SharedKernel.Domain;

/// <summary>
/// The rule that decides whether a typed answer is right (a text question, FR-5): the author lists the accepted answers, and a
/// candidate's answer counts when it matches one of them. Shared by the question bank, which refuses two accepted answers that
/// mean the same thing, and by the exam runtime, which marks the answer, so the two can never disagree about what "the same" is.
/// How forgiving the match is, is the question's <see cref="TextMatchRule"/>.
/// </summary>
public static class TypedAnswer
{
    /// <summary>
    /// The longest answer a candidate may type, after trimming. An answer may run to several paragraphs; this bounds what a candidate can
    /// store, and is also the longest accepted answer an author may list.
    /// </summary>
    public const int MaxLength = 10_000;

    /// <summary>
    /// The form two answers are compared in under <see cref="TextMatchRule.Exact"/>: trimmed, inner whitespace collapsed to one space, lower
    /// case. "  Paris " and "paris" are the same answer, so a candidate is not marked wrong for a stray space or a capital letter.
    /// </summary>
    /// <param name="text">The answer as typed or as the author wrote it; null counts as blank.</param>
    /// <returns>The comparable form, which is empty for a blank answer.</returns>
    public static string Normalize(string? text) =>
        string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    /// <summary>The form two answers are compared in under the given rule; see <see cref="TextMatchRule"/> for what each forgives.</summary>
    /// <param name="text">The answer as typed or as the author wrote it; null counts as blank.</param>
    /// <param name="rule">How strictly answers are compared.</param>
    /// <returns>The comparable form, which is empty for a blank answer.</returns>
    public static string Normalize(string? text, TextMatchRule rule) =>
        rule == TextMatchRule.Lenient ? NormalizeLenient(text) : Normalize(text);

    /// <summary>Whether a typed answer is one of the accepted answers, comparing in the form <see cref="Normalize(string?)"/> gives.</summary>
    /// <param name="typed">What the candidate typed; a blank answer matches nothing.</param>
    /// <param name="accepted">The answers the author accepted.</param>
    /// <returns><see langword="true"/> when the typed answer is one of them.</returns>
    public static bool Matches(string? typed, IEnumerable<string> accepted) => Matches(typed, accepted, TextMatchRule.Exact);

    /// <summary>Whether a typed answer is one of the accepted answers, comparing in the form the question's rule gives.</summary>
    /// <param name="typed">What the candidate typed; a blank answer, or one that is only punctuation under a lenient rule, matches nothing.</param>
    /// <param name="accepted">The answers the author accepted.</param>
    /// <param name="rule">How strictly answers are compared.</param>
    /// <returns><see langword="true"/> when the typed answer is one of them.</returns>
    public static bool Matches(string? typed, IEnumerable<string> accepted, TextMatchRule rule)
    {
        var answer = Normalize(typed, rule);
        return answer.Length > 0 && accepted.Any(a => Normalize(a, rule) == answer);
    }

    // Only punctuation that a short answer can lose without changing, and only accents on Latin letters, are forgiven. Everything else is
    // kept as typed, so the rule errs towards marking an answer wrong and leaving the author to accept another spelling. In particular:
    //  - "." and "," survive between two digits ("3.14", "1,000"), and "-" survives unless it joins two letters, so a number is not changed;
    //  - "+", "=", "/", "%", "<", ">" and currency signs are never touched, because "x+1" and "x-1" are different answers;
    //  - letters of other scripts (Devanagari and the rest) and their combining marks are left alone: stripping a vowel sign changes the word.
    private static string NormalizeLenient(string? text)
    {
        var folded = FoldLatinAccents(text ?? string.Empty);
        var builder = new StringBuilder(folded.Length);
        for (var i = 0; i < folded.Length; i++)
        {
            var c = folded[i];
            var before = i > 0 ? folded[i - 1] : '\0';
            var after = i + 1 < folded.Length ? folded[i + 1] : '\0';
            var insideNumber = char.IsDigit(before) && char.IsDigit(after);

            switch (c)
            {
                // "don't" and "Newton's" lose the apostrophe, so a typographic one from a phone keyboard matches a plain one.
                case '\'' or '’' or '‘' or '`' or '´':
                    break;
                // A full stop is dropped, not spaced: "U.S.A." must become "usa".
                case '.':
                    if (insideNumber)
                        builder.Append(c);
                    break;
                case ',':
                    builder.Append(insideNumber ? ',' : ' ');
                    break;
                case ';' or ':' or '!' or '?' or '"' or '“' or '”' or '„' or '«' or '»' or '(' or ')' or '[' or ']' or '{' or '}' or '_':
                    builder.Append(' ');
                    break;
                // Dashes of every width read as one hyphen, which joins two words ("well-known") but is a minus elsewhere ("-5", "x-1").
                case '-' or '‐' or '‑' or '–' or '—':
                    builder.Append(char.IsLetter(before) && char.IsLetter(after) ? ' ' : '-');
                    break;
                default:
                    builder.Append(char.IsWhiteSpace(c) ? ' ' : c);
                    break;
            }
        }

        // Trimming, collapsing the gaps the dropped marks left, and lower case are the Exact rule's own.
        return Normalize(builder.ToString());
    }

    // Each group is a plain Latin letter followed by the accented letters that fold to it. A table, not string.Normalize: the API runs with
    // invariant globalization (Directory.Build.props), where Normalize cannot take an accent off a letter, and a table gives the same
    // result on every host. Letters that are letters in their own right (ß, æ, œ, þ, ð, the Turkish dotless i) are deliberately left out.
    private static readonly string[] AccentGroups =
    [
        "AÀÁÂÃÄÅĀĂĄǍ", "aàáâãäåāăąǎ", "CÇĆĈĊČ", "cçćĉċč", "DĎĐ", "dďđ", "EÈÉÊËĒĔĖĘĚ", "eèéêëēĕėęě", "GĜĞĠĢ", "gĝğġģ", "HĤĦ", "hĥħ",
        "IÌÍÎÏĨĪĬĮ", "iìíîïĩīĭį", "JĴ", "jĵ", "KĶ", "kķ", "LĹĻĽĿŁ", "lĺļľŀł", "NÑŃŅŇ", "nñńņň", "OÒÓÔÕÖØŌŎŐǑ", "oòóôõöøōŏőǒ",
        "RŔŖŘ", "rŕŗř", "SŚŜŞŠ", "sśŝşš", "TŢŤŦ", "tţťŧ", "UÙÚÛÜŨŪŬŮŰŲǓ", "uùúûüũūŭůűųǔ", "WŴ", "wŵ", "YÝŶŸ", "yýÿŷ", "ZŹŻŽ", "zźżž",
    ];

    private static readonly Dictionary<char, char> AccentFold = AccentGroups
        .SelectMany(group => group.Skip(1).Select(accented => (accented, plain: group[0])))
        .ToDictionary(pair => pair.accented, pair => pair.plain);

    // "café" typed as one character or as a letter and a separate accent (as a Mac or a phone may send it) both become "cafe". A separate
    // accent is dropped only after a basic Latin letter: after any other letter it is part of that letter (a Devanagari vowel sign).
    private static string FoldLatinAccents(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is >= '\u0300' and <= '\u036f' && builder.Length > 0 && IsBasicLatinLetter(builder[^1]))
                continue;

            builder.Append(AccentFold.GetValueOrDefault(c, c));
        }

        return builder.ToString();
    }

    private static bool IsBasicLatinLetter(char c) => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z');
}
