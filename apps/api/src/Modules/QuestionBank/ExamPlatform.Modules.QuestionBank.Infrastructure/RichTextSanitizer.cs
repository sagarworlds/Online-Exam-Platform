using System.Buffers.Text;
using ExamPlatform.Modules.QuestionBank.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Domain;
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
        "p", "br", "strong", "em", "u", "s", "sub", "sup", "ul", "ol", "li", "blockquote", "pre", "code", "img",
    ];

    /// <summary>
    /// The only picture sources accepted: the picture's bytes embedded in the question. A link to another site would
    /// let that site see who reads the question, and would break when the picture is taken down. SVG is left out on
    /// purpose, because an SVG file can carry script.
    /// </summary>
    private static readonly string[] AllowedImageHeaders =
    [
        "data:image/png;base64", "data:image/jpeg;base64", "data:image/gif;base64", "data:image/webp;base64",
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

        // An image whose source was refused is left behind without one; take it out and count it so the caller can say so.
        var rejected = 0;
        foreach (var image in document.QuerySelectorAll("img:not([src])").ToList())
        {
            image.Remove();
            rejected++;
        }

        // Editors leave an empty paragraph after a list or picture so the author can keep typing; it carries nothing.
        while (body.LastElementChild is { LocalName: "p" } last
            && last.TextContent.Trim().Length == 0
            && last.QuerySelector("img") is null)
        {
            last.Remove();
        }

        return new SanitizedRichText(
            body.InnerHtml.Trim(),
            body.TextContent.Trim(),
            document.QuerySelectorAll("img").Length,
            rejected);
    }

    private static bool IsAcceptableImageSource(string? source)
    {
        var comma = source?.IndexOf(',') ?? -1;
        if (source is null || comma < 0)
        {
            return false;
        }

        var header = source[..comma];
        if (!AllowedImageHeaders.Contains(header, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        // The size is checked on the decoded bytes, which is what the limit is about; invalid base64 is refused outright.
        return Base64.IsValid(source.AsSpan(comma + 1), out var decodedLength) && decodedLength <= Question.MaxImageBytes;
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

        // The two attributes a picture needs. "data" is allowed as a scheme only so embedded pictures pass the
        // library's own check; the filter below then refuses every data URL that is not an acceptable picture.
        sanitizer.AllowedAttributes.Add("src");
        sanitizer.AllowedAttributes.Add("alt");
        sanitizer.AllowedSchemes.Add("data");
        sanitizer.FilterUrl += (_, url) =>
        {
            if (!IsAcceptableImageSource(url.OriginalUrl))
            {
                url.SanitizedUrl = null;
            }
        };

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
