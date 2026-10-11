using System.Net;
using System.Text.RegularExpressions;

namespace ExamPlatform.Modules.Analytics.Application;

/// <summary>
/// Turns a question's sanitized HTML into a short line of plain text for the analysis table. The table names a question by its opening words,
/// since the staff member needs to recognise it, not to read it in full.
/// </summary>
public static class QuestionText
{
    /// <summary>The longest preview, in characters, before it is cut and marked with an ellipsis.</summary>
    public const int PreviewLength = 160;

    // Compiled once and kept as plain fields rather than source-generated: the generated helper types would sit outside this layer's namespace.
    private static readonly Regex BlockTagPattern = new(
        @"</?(p|div|br|li|ol|ul|h[1-6]|tr|td|th|table|blockquote|pre)\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex InlineTagPattern = new("<[^>]*>", RegexOptions.Compiled);
    private static readonly Regex WhitespacePattern = new(@"\s+", RegexOptions.Compiled);

    /// <summary>A plain-text preview of the question: tags removed, entities decoded, whitespace collapsed, and cut to <see cref="PreviewLength"/>.</summary>
    /// <remarks>
    /// The output is text, never HTML, so it can be shown without a sanitizer. A block element (a paragraph, a list item, a line break) becomes
    /// a space, so the words either side of it do not run together. An inline one (bold, italics, a subscript) is removed without a space, so
    /// "force</em>?" stays "force?" rather than being split into "force ?".
    /// </remarks>
    /// <param name="html">The question's text as sanitized HTML.</param>
    /// <returns>The preview; empty for a question with no text.</returns>
    public static string Preview(string html)
    {
        var withBlocksSpaced = BlockTagPattern.Replace(html, " ");
        var plain = WebUtility.HtmlDecode(InlineTagPattern.Replace(withBlocksSpaced, string.Empty));
        var text = WhitespacePattern.Replace(plain, " ").Trim();
        return text.Length <= PreviewLength ? text : text[..(PreviewLength - 1)].TrimEnd() + "…";
    }
}
