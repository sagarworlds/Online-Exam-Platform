namespace ExamPlatform.Modules.QuestionBank.Application.Ports;

/// <summary>What is left of an author's rich text once the sanitizer has cleaned it.</summary>
/// <param name="Html">The cleaned HTML: only formatting the bank allows, with nothing a browser could execute.</param>
/// <param name="PlainText">The readable text of <paramref name="Html"/> with all markup removed, trimmed.</param>
/// <param name="ImageCount">How many images <paramref name="Html"/> embeds.</param>
/// <param name="RejectedImageCount">
/// How many images were taken out because they are not an embedded PNG, JPEG, GIF or WebP picture within the size
/// limit (a link to another site, an SVG, a corrupt file, one that is too big). Reported rather than silently
/// dropped, so the author is told instead of saving a question that quietly lost its picture.
/// </param>
public sealed record SanitizedRichText(string Html, string PlainText, int ImageCount, int RejectedImageCount = 0)
{
    /// <summary>Whether there is anything for a candidate to read or look at.</summary>
    public bool HasContent => PlainText.Length > 0 || ImageCount > 0;
}

/// <summary>
/// Cleans author-supplied HTML before it is stored. A question is shown to every candidate, so markup that reaches
/// the database unchecked would let one author run script in other people's browsers; the cleaning therefore
/// happens on the server and is never left to the editor in the author's browser.
/// </summary>
public interface IRichTextSanitizer
{
    /// <summary>Removes everything outside the formatting the question bank allows.</summary>
    /// <param name="html">The HTML as submitted; null or blank gives empty content.</param>
    /// <returns>The cleaned HTML with its readable text and image count.</returns>
    SanitizedRichText Sanitize(string? html);
}
