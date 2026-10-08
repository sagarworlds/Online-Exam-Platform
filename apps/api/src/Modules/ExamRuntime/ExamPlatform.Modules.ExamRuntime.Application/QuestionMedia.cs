using System.Globalization;
using System.Text.RegularExpressions;

namespace ExamPlatform.Modules.ExamRuntime.Application;

/// <summary>A picture that was inside a question's text.</summary>
/// <param name="ContentType">Its media type: PNG, JPEG, GIF or WebP, the only kinds a question may hold.</param>
/// <param name="Bytes">The picture itself.</param>
public sealed record QuestionPicture(string ContentType, byte[] Bytes);

/// <summary>
/// The pictures inside a question's text (FR-53). A question holds each picture's bytes in its HTML, as a data URL, so one diagram can
/// outweigh all the words around it, and a candidate on a slow connection waits for it before seeing any question at all. In low-bandwidth
/// mode the attempt is sent with the pictures taken out (<see cref="Detach"/>), each left as a marker the page can fetch on demand
/// (<see cref="Find"/> serves it), so the words arrive first and a picture arrives when it is wanted. Only the question's text can hold
/// pictures: an option is plain text.
/// </summary>
public static class QuestionMedia
{
    private const string KeyPrefix = "q-";

    // The sanitizer allows exactly this and nothing else with a src (RichTextSanitizer): an embedded PNG, JPEG, GIF or WebP, written in
    // double quotes. Anything else a question held was already removed when it was saved. A plain Regex and not [GeneratedRegex], whose
    // generated type sits outside the module's namespace, which the architecture rules refuse.
    private static readonly Regex InlinePicture = new(
        @"\bsrc=""data:(image/(?:png|jpeg|gif|webp));base64,([A-Za-z0-9+/=]+)""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Takes the pictures out of a question's text. Each picture's <c>src</c> becomes a class naming it (<c>lazy-media--q-{n}</c>, counting
    /// from 0) and giving its size (<c>lazy-bytes--{bytes}</c>), so the page can offer it with its size and fetch it when asked. A class
    /// is used because it survives the page's own sanitizer, which drops other custom attributes.
    /// </summary>
    /// <param name="html">The sanitized text of a question.</param>
    /// <returns>The text, with no picture data in it.</returns>
    public static string Detach(string html)
    {
        if (string.IsNullOrEmpty(html) || !html.Contains("data:image/", StringComparison.Ordinal))
            return html;

        var index = 0;
        return InlinePicture.Replace(html, match =>
            $"class=\"lazy-media lazy-media--{KeyPrefix}{index++} lazy-bytes--{DecodedLength(match.Groups[2].Value)}\"");
    }

    /// <summary>Finds a picture in a question's text, counting them as <see cref="Detach"/> does.</summary>
    /// <param name="html">The text.</param>
    /// <param name="index">Which picture, from 0.</param>
    /// <returns>The picture, or <see langword="null"/> when the text has no such one or its data is not valid.</returns>
    public static QuestionPicture? Find(string html, int index)
    {
        if (string.IsNullOrEmpty(html) || index < 0)
            return null;

        var seen = 0;
        foreach (Match match in InlinePicture.Matches(html))
        {
            if (seen++ != index)
                continue;

            try
            {
                return new QuestionPicture(match.Groups[1].Value, Convert.FromBase64String(match.Groups[2].Value));
            }
            catch (FormatException)
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>Reads a picture's key, as <see cref="Detach"/> names them: <c>q-</c> and the picture's number.</summary>
    /// <param name="key">The key, such as <c>q-0</c>.</param>
    /// <param name="index">The number part.</param>
    /// <returns>Whether the key is one of that shape.</returns>
    public static bool TryParseKey(string key, out int index)
    {
        index = 0;
        return key.StartsWith(KeyPrefix, StringComparison.Ordinal)
               && int.TryParse(key.AsSpan(KeyPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out index);
    }

    private static long DecodedLength(string base64)
    {
        var padding = base64.EndsWith("==", StringComparison.Ordinal) ? 2 : base64.EndsWith('=') ? 1 : 0;
        return (base64.Length / 4 * 3) - padding;
    }
}
