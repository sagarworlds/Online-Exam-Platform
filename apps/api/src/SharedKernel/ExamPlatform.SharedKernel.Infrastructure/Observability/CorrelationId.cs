namespace ExamPlatform.SharedKernel.Infrastructure.Observability;

/// <summary>
/// The request correlation id (NFR-9): one value that a caller can quote when reporting a failure. It appears in the
/// <see cref="HeaderName"/> response header, in the problem details' <c>traceId</c>, on every log line the request writes,
/// and in the audit trail's <c>CorrelationId</c>.
/// </summary>
/// <remarks>
/// A caller may send its own id in <see cref="HeaderName"/>, so a gateway or a support tool can follow one request across
/// systems. An id that does not meet <see cref="IsAcceptable"/> is replaced, never rejected: a tracing header must not fail a
/// request. The rule also keeps the id safe to write into a log (no line breaks, so no forged log lines) and inside the audit
/// trail's 200-character column, so storing it can never fail.
/// </remarks>
public static class CorrelationId
{
    /// <summary>The request and response header that carries the id.</summary>
    public const string HeaderName = "X-Correlation-Id";

    /// <summary>
    /// The longest id kept. Well under the audit trail's 200-character <c>CorrelationId</c> column, so an accepted id always fits.
    /// </summary>
    public const int MaxLength = 128;

    /// <summary>
    /// Whether a caller's id may be used as this request's id: 1 to <see cref="MaxLength"/> letters, digits, dots, hyphens or
    /// underscores. Those characters need no escaping in a header, a URL, a log line or a JSON string.
    /// </summary>
    /// <param name="candidate">The id the caller sent, or null when it sent none.</param>
    /// <returns>True when the id is safe to use as-is.</returns>
    public static bool IsAcceptable(string? candidate) =>
        candidate is { Length: > 0 and <= MaxLength } && candidate.All(IsAllowedCharacter);

    /// <summary>A new id for a request that did not bring an acceptable one: a 32-character random hex string.</summary>
    /// <returns>A fresh, unguessable id.</returns>
    public static string NewId() => Guid.NewGuid().ToString("N");

    private static bool IsAllowedCharacter(char c) =>
        c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-' or '_' or '.';
}
