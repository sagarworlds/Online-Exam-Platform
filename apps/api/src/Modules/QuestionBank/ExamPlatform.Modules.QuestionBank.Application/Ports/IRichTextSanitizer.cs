namespace ExamPlatform.Modules.QuestionBank.Application.Ports;

/// <summary>What is left of an author's rich text once the sanitizer has cleaned it.</summary>
/// <param name="Html">The cleaned HTML: only formatting the bank allows, with nothing a browser could execute.</param>
/// <param name="PlainText">The readable text of <paramref name="Html"/> with all markup removed, trimmed.</param>
/// <param name="ImageCount">How many images <paramref name="Html"/> embeds.</param>
public sealed record SanitizedRichText(string Html, string PlainText, int ImageCount)
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
