using ExamPlatform.Modules.QuestionBank.Application.Ports;
using Ganss.Xss;

namespace ExamPlatform.Modules.QuestionBank.Infrastructure;

/// <summary>
/// The question bank's <see cref="IRichTextSanitizer"/>, built on the HtmlSanitizer library. It works from an
/// allowlist: only the formatting a question needs survives, and every attribute, style, class, link and script is
/// dropped. An allowlist is the safe direction, because a tag or attribute the author's browser invents next year is
/// refused by default instead of waiting for someone to notice it and add it to a blocklist.
/// </summary>
public sealed class RichTextSanitizer : IRichTextSanitizer
{
    /// <summary>The formatting a question may carry: text emphasis, sub/superscripts, lists, quotes and code.</summary>
    private static readonly string[] AllowedTags =
    [
        "p", "br", "strong", "em", "u", "s", "sub", "sup", "ul", "ol", "li", "blockquote", "pre", "code",
    ];

    /// <summary>
    /// Tags whose content is code or markup rather than text: it is discarded with the tag instead of surviving as
    /// visible text (a removed <c>&lt;script&gt;alert(1)&lt;/script&gt;</c> must not leave "alert(1)" in the question).
    /// </summary>
    private static readonly HashSet<string> DiscardContentTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "noscript", "iframe", "object", "embed", "template", "svg", "math", "textarea", "title", "head",
    };

    private readonly HtmlSanitizer sanitizer = Build();

    /// <inheritdoc />
    public SanitizedRichText Sanitize(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return new SanitizedRichText(string.Empty, string.Empty, 0);
        }

        var document = sanitizer.SanitizeDom(html);
        var body = document.Body;
        if (body is null)
        {
            return new SanitizedRichText(string.Empty, string.Empty, 0);
        }

        return new SanitizedRichText(
            body.InnerHtml.Trim(),
            body.TextContent.Trim(),
            document.QuerySelectorAll("img").Length);
    }

    private static HtmlSanitizer Build()
    {
        var sanitizer = new HtmlSanitizer
        {
            // Text inside a tag that is not allowed (a <div> or <span> pasted in from a web page or a document)
            // is kept as plain text instead of vanishing along with its wrapper.
            KeepChildNodes = true,
            AllowDataAttributes = false,
        };

        // The defaults allow a broad set (style, class, links...); start from nothing and add only what is listed.
        sanitizer.AllowedTags.Clear();
        sanitizer.AllowedAttributes.Clear();
        sanitizer.AllowedClasses.Clear();
        sanitizer.AllowedCssProperties.Clear();
        sanitizer.AllowedSchemes.Clear();

        foreach (var tag in AllowedTags)
        {
            sanitizer.AllowedTags.Add(tag);
        }

        sanitizer.RemovingTag += (_, removal) =>
        {
            if (DiscardContentTags.Contains(removal.Tag.LocalName))
            {
                removal.Tag.InnerHtml = string.Empty;
            }
        };

        return sanitizer;
    }
}
