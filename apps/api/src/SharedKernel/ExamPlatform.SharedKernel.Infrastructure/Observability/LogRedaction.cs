using System.Text.RegularExpressions;

namespace ExamPlatform.SharedKernel.Infrastructure.Observability;

/// <summary>
/// Removes personal data and secrets from log output (NFR-6: PII masking in logs). The formatter applies it to every message,
/// exception, scope and property it writes, so a value that reaches a log call by accident is still masked. It is a last line of
/// defence, not a licence: the code that logs should not pass such values in the first place.
/// </summary>
/// <remarks>
/// Two checks, because a value can be identified by what it looks like or by what it is called. <see cref="RedactText"/> looks
/// for shapes (an e-mail address, a phone number, a token, a labelled password) inside free text, such as an exception message
/// that echoes a rejected address. <see cref="IsSensitiveName"/> covers structured properties whose name says what they hold.
/// Masked contact details, such as <c>a***@example.com</c> or <c>**********10</c>, are left alone: they are what the platform
/// writes on purpose.
/// </remarks>
public static class LogRedaction
{
    /// <summary>What a sensitive property's value becomes.</summary>
    public const string Redacted = "[redacted]";

    /// <summary>
    /// Each rule is linear and has a timeout. A log line is never allowed to stall the request that wrote it, and when a rule
    /// does time out the text is withheld rather than written unchecked (see <see cref="RedactText"/>).
    /// </summary>
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    private static readonly (Regex Pattern, string Replacement)[] TextRules =
    [
        // E-mail addresses: a complete one. Masked forms ("a***@x.com") have no plain local part and do not match.
        (Rule(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9\-]+(?:\.[A-Za-z0-9\-]+)+"), "[redacted-email]"),

        // A JSON Web Token, as used for access and refresh tokens: three base64url segments, the first starting "eyJ" ({"...).
        (Rule(@"\beyJ[A-Za-z0-9_\-]{4,}\.[A-Za-z0-9_\-]{4,}\.[A-Za-z0-9_\-]{4,}"), "[redacted-token]"),

        // A bearer credential in an Authorization header or a message that quotes one.
        (Rule(@"\bBearer\s+[A-Za-z0-9._~+/=\-]{8,}", RegexOptions.IgnoreCase), "Bearer [redacted-token]"),

        // A labelled secret: "password=...", "token: ..." or "hub.verify_token=...". The label is kept so the line still says what was
        // withheld. The label may follow an underscore, a dot or a hyphen (a query parameter such as hub.verify_token), but not a letter
        // or digit, so a longer word that merely ends in a label is left alone.
        (Rule(
                @"(?<![A-Za-z0-9])(password|passwd|pwd|secret|token|access[_-]?token|refresh[_-]?token|api[_-]?key|authorization|otp|answer|cookie)(\s*[:=]\s*)(""[^""]*""|'[^']*'|[^\s,;&}\]]+)",
                RegexOptions.IgnoreCase),
            "$1$2" + Redacted),

        // International format: a plus sign, then digits, with optional spaces or hyphens.
        (Rule(@"\+\d[\d\s\-]{7,}\d"), "[redacted-phone]"),

        // Indian mobile grouping, "98765 43210" or "98765-43210".
        (Rule(@"(?<!\d)\d{5}[\s\-]\d{5}(?!\d)"), "[redacted-phone]"),

        // A bare run of 10 to 15 digits: a phone number written without separators, or an account number. Dates, durations and
        // counts are shorter, so they are left readable.
        (Rule(@"(?<![\w.\-])\d{10,15}(?![\w.\-])"), "[redacted-number]"),
    ];

    /// <summary>Structured property names that always hold personal data or a secret, normalised to lower case without separators.</summary>
    private static readonly HashSet<string> SensitiveNames = new(StringComparer.Ordinal)
    {
        "password", "passwd", "pwd", "secret", "token", "accesstoken", "refreshtoken", "apikey", "authorization", "cookie",
        "otp", "otpcode", "answer", "answers", "questiontext", "requestbody", "body",
        "email", "emailaddress", "phone", "phonenumber", "mobile",
    };

    /// <summary>
    /// Masks personal data and secrets in a piece of log text.
    /// </summary>
    /// <param name="text">The text to check; null or empty is returned as an empty string.</param>
    /// <returns>The text with each matching value replaced by a marker. If a rule runs out of time, the whole text is replaced by a
    /// marker instead, so nothing unchecked is written.</returns>
    public static string RedactText(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        try
        {
            var result = text;
            foreach (var (pattern, replacement) in TextRules)
            {
                result = pattern.Replace(result, replacement);
            }

            return result;
        }
        catch (RegexMatchTimeoutException)
        {
            return "[redacted: text could not be checked in time]";
        }
    }

    /// <summary>
    /// Whether a structured property holds personal data or a secret by its name alone, so its value is written as
    /// <see cref="Redacted"/> whatever it is. Case and separators are ignored: <c>OtpCode</c>, <c>otp_code</c> and <c>OTP-CODE</c>
    /// all match.
    /// </summary>
    /// <param name="name">The property name as the log call gave it.</param>
    /// <returns>True when the value must not be written.</returns>
    public static bool IsSensitiveName(string name) =>
        SensitiveNames.Contains(Normalise(name));

    private static string Normalise(string name) =>
        name.Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

    private static Regex Rule(string pattern, RegexOptions options = RegexOptions.None) =>
        new(pattern, options | RegexOptions.CultureInvariant, MatchTimeout);
}
