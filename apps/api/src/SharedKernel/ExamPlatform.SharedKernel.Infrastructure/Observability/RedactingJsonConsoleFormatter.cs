using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace ExamPlatform.SharedKernel.Infrastructure.Observability;

/// <summary>
/// Writes each log entry as one line of JSON on the console (NFR-9), with personal data and secrets masked by
/// <see cref="LogRedaction"/>. The line holds a timestamp in UTC, the level, the category, the event id, the correlation id,
/// the rendered message, any exception, the message's own named values and any other log scopes.
/// </summary>
/// <remarks>
/// One line per entry is the point: a log collector splits on newlines, so a message that contains a line break cannot forge a
/// second entry, because JSON escapes it. It is a formatter of the standard console provider, not a second logging stack, so the
/// <c>Logging</c> configuration's levels and filters apply to it unchanged.
/// </remarks>
public sealed class RedactingJsonConsoleFormatter : ConsoleFormatter
{
    /// <summary>The name the console provider selects this formatter by.</summary>
    public const string FormatterName = "exam-platform-json";

    /// <summary>The scope key that carries the request correlation id; it is written as the top-level <c>correlationId</c>.</summary>
    public const string CorrelationIdKey = "CorrelationId";

    private const string OriginalFormatKey = "{OriginalFormat}";

    /// <summary>
    /// Scope keys that are not written. ASP.NET Core opens a scope with the raw request path and the query string on every request.
    /// A path can carry a value a caller meant to keep private, and the query string can carry a secret (the WhatsApp verify token),
    /// so neither is written. The correlation id and the route template identify the request without them.
    /// </summary>
    private static readonly HashSet<string> OmittedScopeKeys = new(StringComparer.Ordinal)
    {
        "RequestPath", "QueryString", CorrelationIdKey,
    };

    /// <summary>The message's template, which is a description of the call rather than a value to write.</summary>
    private static readonly HashSet<string> OmittedPropertyKeys = new(StringComparer.Ordinal) { OriginalFormatKey };

    /// <summary>Creates the formatter under <see cref="FormatterName"/>.</summary>
    public RedactingJsonConsoleFormatter() : base(FormatterName)
    {
    }

    /// <inheritdoc />
    public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        var scopes = CollectScopes(scopeProvider);
        var buffer = new ArrayBufferWriter<byte>();

        // Log lines are read in a log viewer, not embedded in HTML, so the HTML-safe escaping would only turn an apostrophe or a name in
        // Devanagari into an unreadable \u escape. Control characters, the line break included, are still escaped.
        using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            json.WriteStartObject();
            json.WriteString("timestamp", DateTimeOffset.UtcNow);
            json.WriteString("level", logEntry.LogLevel.ToString());
            json.WriteString("category", logEntry.Category);
            json.WriteNumber("eventId", logEntry.EventId.Id);

            if (FindScopeValue(scopes, CorrelationIdKey) is { } correlationId)
            {
                json.WriteString("correlationId", correlationId);
            }

            json.WriteString("message", LogRedaction.RedactText(logEntry.Formatter(logEntry.State, logEntry.Exception)));

            if (logEntry.Exception is not null)
            {
                json.WriteString("exception", LogRedaction.RedactText(logEntry.Exception.ToString()));
            }

            WriteNamedValues(json, "properties", logEntry.State as IEnumerable<KeyValuePair<string, object?>>, OmittedPropertyKeys);
            WriteNamedValues(json, "scope", scopes, OmittedScopeKeys);

            json.WriteEndObject();
        }

        textWriter.WriteLine(Encoding.UTF8.GetString(buffer.WrittenSpan));
    }

    /// <summary>Gathers the scopes open around the log call, flattened into named values.</summary>
    /// <param name="scopeProvider">The provider the logger uses, or null when no scope is open.</param>
    /// <returns>Every scope's named values, in the order they were opened.</returns>
    private static List<KeyValuePair<string, object?>> CollectScopes(IExternalScopeProvider? scopeProvider)
    {
        var scopes = new List<KeyValuePair<string, object?>>();
        scopeProvider?.ForEachScope(static (scope, collected) => AddScope(scope, collected), scopes);
        return scopes;
    }

    private static void AddScope(object? scope, List<KeyValuePair<string, object?>> collected)
    {
        switch (scope)
        {
            case IEnumerable<KeyValuePair<string, object?>> named:
                collected.AddRange(named);
                break;
            case not null:
                collected.Add(new KeyValuePair<string, object?>("Scope", scope));
                break;
        }
    }

    private static string? FindScopeValue(List<KeyValuePair<string, object?>> scopes, string key)
    {
        foreach (var pair in scopes)
        {
            if (pair.Key == key)
            {
                return pair.Value?.ToString();
            }
        }

        return null;
    }

    /// <summary>Writes a named object of values, leaving out the keys that are not to be written.</summary>
    /// <param name="json">The writer the line is built with.</param>
    /// <param name="objectName">The name of the object in the line.</param>
    /// <param name="values">The values to write, or null for none.</param>
    /// <param name="omittedKeys">Keys that are never written.</param>
    private static void WriteNamedValues(
        Utf8JsonWriter json,
        string objectName,
        IEnumerable<KeyValuePair<string, object?>>? values,
        IReadOnlySet<string> omittedKeys)
    {
        if (values is null)
        {
            return;
        }

        var pairs = values.Where(pair => !omittedKeys.Contains(pair.Key)).ToList();
        if (pairs.Count == 0)
        {
            return;
        }

        json.WriteStartObject(objectName);
        foreach (var pair in pairs)
        {
            WriteNamedValue(json, pair.Key, pair.Value);
        }

        json.WriteEndObject();
    }

    private static void WriteNamedValue(Utf8JsonWriter json, string name, object? value)
    {
        json.WritePropertyName(name);
        if (LogRedaction.IsSensitiveName(name))
        {
            json.WriteStringValue(LogRedaction.Redacted);
            return;
        }

        switch (value)
        {
            case null:
                json.WriteNullValue();
                break;
            case bool flag:
                json.WriteBooleanValue(flag);
                break;
            case int number:
                json.WriteNumberValue(number);
                break;
            case long number:
                json.WriteNumberValue(number);
                break;
            default:
                json.WriteStringValue(LogRedaction.RedactText(Convert.ToString(value, CultureInfo.InvariantCulture)));
                break;
        }
    }
}
