using System.Text.Json;
using ExamPlatform.SharedKernel.Infrastructure.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExamPlatform.SharedKernel.UnitTests;

/// <summary>What one log entry looks like on the console: one line of JSON, with personal data masked (NFR-9, NFR-6).</summary>
public class RedactingJsonConsoleFormatterTests
{
    /// <summary>Writes one entry through the formatter and parses the line it wrote.</summary>
    private static (string Raw, JsonElement Json) Write<TState>(
        TState state,
        Func<TState, Exception?, string> formatter,
        Exception? exception = null,
        params object[] scopes)
    {
        var writer = new StringWriter();
        new RedactingJsonConsoleFormatter().Write(
            new LogEntry<TState>(LogLevel.Warning, "Test.Category", new EventId(7), state, exception, formatter),
            new FixedScopes(scopes),
            writer);

        var raw = writer.ToString();
        using var document = JsonDocument.Parse(raw);
        return (raw, document.RootElement.Clone());
    }

    [Fact]
    public void EachEntry_IsOneLineOfJson_AndALineBreakInTheMessageCannotForgeASecondEntry()
    {
        var (raw, json) = Write("line one\n{\"level\":\"Error\",\"message\":\"forged\"}", (s, _) => s);

        Assert.Equal(0, raw.TrimEnd('\r', '\n').Count(c => c == '\n'));
        Assert.Equal("line one\n{\"level\":\"Error\",\"message\":\"forged\"}", json.GetProperty("message").GetString());
    }

    [Fact]
    public void TheHeaderFields_AreLevelCategoryEventIdAndUtcTimestamp()
    {
        var (_, json) = Write("hello", (s, _) => s);

        Assert.Equal("Warning", json.GetProperty("level").GetString());
        Assert.Equal("Test.Category", json.GetProperty("category").GetString());
        Assert.Equal(7, json.GetProperty("eventId").GetInt32());
        Assert.Equal(TimeSpan.Zero, DateTimeOffset.Parse(json.GetProperty("timestamp").GetString()!).Offset);
    }

    [Fact]
    public void TheMessage_IsRedacted()
    {
        var (_, json) = Write("Mail to student@example.com failed", (s, _) => s);

        Assert.Equal("Mail to [redacted-email] failed", json.GetProperty("message").GetString());
    }

    [Fact]
    public void TheException_IsRedacted()
    {
        var exception = new InvalidOperationException("Recipient +91 98765 43210 was refused.");

        var (_, json) = Write("send failed", (s, _) => s, exception);

        var logged = json.GetProperty("exception").GetString()!;
        Assert.Contains("Recipient [redacted-phone] was refused.", logged, StringComparison.Ordinal);
        Assert.DoesNotContain("98765", logged, StringComparison.Ordinal);
    }

    [Fact]
    public void ANamedValue_WhoseNameIsSensitive_IsWrittenAsRedacted_WhateverItHolds()
    {
        var state = new List<KeyValuePair<string, object?>>
        {
            new("{OriginalFormat}", "Login with {Password}"),
            new("Password", "hunter2"),
            new("Reason", "rejected mail to student@example.com"),
        };

        // The rendered message does not contain the password: a message template that names it is a call site's responsibility.
        var (raw, json) = Write(state, (_, _) => "Login attempted", null);

        var properties = json.GetProperty("properties");
        Assert.Equal("[redacted]", properties.GetProperty("Password").GetString());
        Assert.Equal("rejected mail to [redacted-email]", properties.GetProperty("Reason").GetString());
        Assert.False(properties.TryGetProperty("{OriginalFormat}", out _), "The template itself is not a value to log.");
        Assert.DoesNotContain("hunter2", raw, StringComparison.Ordinal);
    }

    [Fact]
    public void NumbersAndBooleans_KeepTheirJsonType()
    {
        var state = new List<KeyValuePair<string, object?>>
        {
            new("StatusCode", 404),
            new("Count", 12L),
            new("Accepted", true),
        };

        var (_, json) = Write(state, (_, _) => "n/a", null);

        Assert.Equal(JsonValueKind.Number, json.GetProperty("properties").GetProperty("StatusCode").ValueKind);
        Assert.Equal(12L, json.GetProperty("properties").GetProperty("Count").GetInt64());
        Assert.True(json.GetProperty("properties").GetProperty("Accepted").GetBoolean());
    }

    [Fact]
    public void TheCorrelationId_IsATopLevelField_AndOtherScopesAreKeptUnderScope()
    {
        var correlation = new Dictionary<string, object> { [CorrelationIdMiddleware.ScopeKey] = "req-42" };
        var batch = new Dictionary<string, object> { ["BatchId"] = "batch-7" };

        var (_, json) = Write("hello", (s, _) => s, null, correlation, batch);

        Assert.Equal("req-42", json.GetProperty("correlationId").GetString());
        Assert.Equal("batch-7", json.GetProperty("scope").GetProperty("BatchId").GetString());
        Assert.False(json.GetProperty("scope").TryGetProperty(CorrelationIdMiddleware.ScopeKey, out _));
    }

    [Fact]
    public void TheRawPathAndQueryString_ThatTheFrameworkScopes_AreNotWritten()
    {
        var framework = new Dictionary<string, object>
        {
            ["RequestPath"] = "/v1/invites/secret-path-value",
            ["QueryString"] = "?hub.verify_token=s3cr3t",
            ["RequestId"] = "req-9",
        };

        var (raw, json) = Write("hello", (s, _) => s, null, framework);

        var scope = json.GetProperty("scope");
        Assert.False(scope.TryGetProperty("RequestPath", out _));
        Assert.False(scope.TryGetProperty("QueryString", out _));
        Assert.Equal("req-9", scope.GetProperty("RequestId").GetString());
        Assert.DoesNotContain("secret-path-value", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("s3cr3t", raw, StringComparison.Ordinal);
    }

    [Fact]
    public void AMessageWithoutScopes_HasNoCorrelationIdOrScopeField()
    {
        var (_, json) = Write("hello", (s, _) => s);

        Assert.False(json.TryGetProperty("correlationId", out _));
        Assert.False(json.TryGetProperty("scope", out _));
        Assert.False(json.TryGetProperty("properties", out _));
        Assert.False(json.TryGetProperty("exception", out _));
    }

    [Fact]
    public void TheFormatter_IsRegisteredUnderItsOwnName()
    {
        Assert.Equal(RedactingJsonConsoleFormatter.FormatterName, new RedactingJsonConsoleFormatter().Name);
    }
}
