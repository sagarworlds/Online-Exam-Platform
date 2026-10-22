using System.Net;
using System.Text.RegularExpressions;

namespace ExamPlatform.Modules.ExamRuntime.Application;

/// <summary>
/// The readable words of a question, for sending to the AI evaluator. A question is stored as sanitized HTML, which would waste the
/// evaluator's attention on markup and could carry a picture's address it cannot use.
/// </summary>
public static class QuestionPlainText
{
    // A plain Regex, not a generated one: a generated regex adds a type to the layer's namespace that the architecture tests do not allow.
    private static readonly Regex Picture = new(@"<img\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex LineEnd = new(@"<br\s*/?>|</(p|div|li|tr|h[1-6]|blockquote)>", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Tag = new(@"<[^>]*>", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Spaces = new(@"[ \t\r\f\v]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Lines = new(@"\s*\n\s*", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Turns a question's HTML into plain text: tags removed, a picture named as one, paragraphs kept apart, entities decoded.</summary>
    /// <param name="html">The question as stored; null or blank gives an empty string.</param>
    public static string FromHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var text = Picture.Replace(html, " [picture] ");
        text = LineEnd.Replace(text, "\n");
        text = Tag.Replace(text, string.Empty);
        text = WebUtility.HtmlDecode(text);
        text = Spaces.Replace(text, " ");
        return Lines.Replace(text, "\n").Trim();
    }
}
