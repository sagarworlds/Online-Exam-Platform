using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamPlatform.Modules.ExamRuntime.Infrastructure.AiEvaluation;

/// <summary>
/// Judges typed answers with Google's Gemini service: one request per answer carrying the question, the accepted answers and the candidate's
/// answer, and a verdict, right or wrong, coming back as JSON in a form the service is told to keep to.
/// </summary>
/// <remarks>
/// <para>
/// A call tries up to <see cref="GeminiOptions.MaxTries"/> times with a doubling wait, and a try is repeated when the failure is likely to
/// pass: no connection, no answer in time, a busy or overloaded service (408, 409, 429, 5xx), or an answer that holds no usable verdict. A failure
/// that waiting cannot cure (a key the service refuses, a model it does not have, a request it cannot read) stops at once and is marked
/// permanent, so the caller does not spend an hour on it. When the tries run out the call fails and the caller queues the answer to be
/// tried again later: a result waits for its verdict.
/// </para>
/// <para>
/// One reply is a verdict of its own, so it is not retried: the service blocked the answer for its safety rules. It is marked wrong, with
/// that said as the reason, because asking the same words again is blocked the same way and an exam cannot wait for ever for it.
/// </para>
/// <para>
/// The key travels in a header and nothing but the status of a failure is logged: not the key, not the question, not the key's answers and
/// not what the candidate typed.
/// </para>
/// </remarks>
public sealed class GeminiTextAnswerEvaluator(
    HttpClient httpClient,
    IOptions<GeminiOptions> options,
    ILogger<GeminiTextAnswerEvaluator> logger,
    Func<TimeSpan, CancellationToken, Task>? wait = null) : ITextAnswerEvaluator
{
    // The four categories the service's safety setting names. Each is given the same threshold.
    private static readonly string[] HarmCategories =
    [
        "HARM_CATEGORY_HARASSMENT", "HARM_CATEGORY_HATE_SPEECH", "HARM_CATEGORY_SEXUALLY_EXPLICIT", "HARM_CATEGORY_DANGEROUS_CONTENT",
    ];

    // Why the service may give no answer at all, which asking again does not change.
    private static readonly HashSet<string> BlockedFinishReasons =
        new(StringComparer.OrdinalIgnoreCase) { "SAFETY", "PROHIBITED_CONTENT", "BLOCKLIST", "SPII", "RECITATION", "IMAGE_SAFETY" };

    private readonly Func<TimeSpan, CancellationToken, Task> _wait = wait ?? ((delay, token) => Task.Delay(delay, token));

    /// <inheritdoc />
    public bool IsEnabled => options.Value.IsEnabled;

    /// <inheritdoc />
    public async Task<TextEvaluation> EvaluateAsync(TextEvaluationRequest request, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.IsEnabled)
            throw new InvalidOperationException($"The AI evaluator is not set up: {GeminiOptions.SectionName}:ApiKey is empty or {GeminiOptions.SectionName}:Enabled is false.");

        var address = new Uri($"{settings.BaseUrl.TrimEnd('/')}/{settings.ApiVersion.Trim('/')}/models/{settings.ModelId}:generateContent");
        var body = BuildBody(request, settings);

        TextEvaluationException? last = null;
        for (var attempt = 1; attempt <= settings.MaxTries; attempt++)
        {
            var outcome = await TryOnceAsync(address, body, settings, cancellationToken);
            if (outcome.Evaluation is { } evaluation)
                return evaluation;

            var failure = outcome.Failure!;
            if (failure.IsPermanent)
                throw failure;

            last = failure;
            if (attempt == settings.MaxTries)
                break;

            var delay = DelayBefore(attempt + 1, outcome.RetryAfter, settings);
            logger.LogWarning(
                "Try {Attempt} of {MaxTries} at asking the AI evaluator failed ({Reason}); trying again in {Delay:0.##} seconds.",
                attempt, settings.MaxTries, failure.Message, delay.TotalSeconds);
            await _wait(delay, cancellationToken);
        }

        throw new TextEvaluationException($"{last!.Message} It was tried {settings.MaxTries} times.", isPermanent: false, last);
    }

    // The wait before try number `attempt` (the second try waits the base delay, the third twice that, and so on), or what the service asked
    // for when it said how long to wait, either way no longer than the most a call is willing to wait.
    private static TimeSpan DelayBefore(int attempt, TimeSpan? requested, GeminiOptions settings)
    {
        var ceiling = TimeSpan.FromSeconds(settings.RetryMaxDelaySeconds);
        var doubling = TimeSpan.FromSeconds(settings.RetryBaseDelaySeconds * Math.Pow(2, attempt - 2));
        var wanted = requested ?? doubling;
        return wanted > ceiling ? ceiling : wanted;
    }

    private readonly record struct Outcome(TextEvaluation? Evaluation, TextEvaluationException? Failure, TimeSpan? RetryAfter = null)
    {
        public static Outcome Done(TextEvaluation evaluation) => new(evaluation, null);

        public static Outcome Transient(string reason, TimeSpan? retryAfter = null) => new(null, new TextEvaluationException(reason, isPermanent: false), retryAfter);

        public static Outcome Permanent(string reason) => new(null, new TextEvaluationException(reason, isPermanent: true));
    }

    private async Task<Outcome> TryOnceAsync(Uri address, string body, GeminiOptions settings, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, address) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        // In a header, never in the address: nothing that names the address, such as a log line or an exception message, can then carry the key.
        message.Headers.Add("x-goog-api-key", settings.ApiKey);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.RequestTimeoutSeconds));

        try
        {
            using var response = await httpClient.SendAsync(message, timeout.Token);
            var content = await response.Content.ReadAsStringAsync(timeout.Token);
            return response.IsSuccessStatusCode ? ReadReply(content, settings) : ReadFailure(response, content);
        }
        catch (HttpRequestException)
        {
            return Outcome.Transient("The AI service could not be reached.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The request's own time limit, as distinct from the caller asking to stop.
            return Outcome.Transient("The AI service did not answer in time.");
        }
    }

    private static string BuildBody(TextEvaluationRequest request, GeminiOptions settings)
    {
        var payload = new Dictionary<string, object?>
        {
            ["systemInstruction"] = new { parts = new[] { new { text = TextEvaluationPrompt.SystemInstruction } } },
            ["contents"] = new[] { new { role = "user", parts = new[] { new { text = TextEvaluationPrompt.Build(request) } } } },
            ["generationConfig"] = new
            {
                // The same answer is marked the same way each time as far as the service allows.
                temperature = 0,
                responseMimeType = "application/json",
                responseSchema = new
                {
                    type = "OBJECT",
                    properties = new
                    {
                        verdict = new { type = "STRING", @enum = new[] { "correct", "wrong" } },
                        reason = new { type = "STRING" },
                    },
                    required = new[] { "verdict" },
                },
            },
        };

        if (!string.IsNullOrWhiteSpace(settings.SafetyThreshold))
        {
            var threshold = settings.SafetyThreshold.Trim();
            payload["safetySettings"] = HarmCategories.Select(category => new { category, threshold }).ToArray();
        }

        return JsonSerializer.Serialize(payload);
    }

    private Outcome ReadFailure(HttpResponseMessage response, string content)
    {
        var code = (int)response.StatusCode;
        var (status, message, retryDelay) = ReadError(content);
        var summary = $"The AI service answered {code}{(status is null ? string.Empty : $" ({status})")}.";

        // 408 and 409 are the service asking to be tried again, 429 is its rate limit, and 5xx is its own trouble. Every other refusal (a key it
        // does not accept, a model it does not have, a request it cannot read) will be refused again until something is changed.
        if (code is 408 or 409 or 429 or >= 500)
            return Outcome.Transient(summary, RetryAfter(response, retryDelay));

        // Google says why, in words that name a setting and never the request's content, so an operator can be told the real reason.
        logger.LogError(
            "The AI evaluator was refused (status {StatusCode} {ErrorStatus}): {Reason}. Asking again will not help until this is changed.",
            code, status ?? "unknown", Shorten(message) ?? "no reason given");
        return Outcome.Permanent(summary);
    }

    private Outcome ReadReply(string content, GeminiOptions settings)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(content);
        }
        catch (JsonException)
        {
            return Outcome.Transient("The AI service's answer could not be read.");
        }

        using (document)
        {
            var root = document.RootElement;
            var model = root.TryGetProperty("modelVersion", out var version) && version.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(version.GetString())
                ? version.GetString()!
                : settings.ModelId;

            // The whole prompt was refused: no candidate at all, and the service says why.
            if (root.TryGetProperty("promptFeedback", out var feedback) && feedback.ValueKind == JsonValueKind.Object
                && feedback.TryGetProperty("blockReason", out var blockReason) && blockReason.ValueKind == JsonValueKind.String)
                return Blocked(blockReason.GetString(), model);

            if (!root.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() == 0)
                return Outcome.Transient("The AI service's answer held no reply.");

            var candidate = candidates[0];
            var finishReason = candidate.TryGetProperty("finishReason", out var finish) && finish.ValueKind == JsonValueKind.String ? finish.GetString() : null;
            if (finishReason is not null && BlockedFinishReasons.Contains(finishReason))
                return Blocked(finishReason, model);

            var text = ReplyText(candidate);
            if (string.IsNullOrWhiteSpace(text))
                return Outcome.Transient($"The AI service's answer held no text{(finishReason is null ? string.Empty : $" (it stopped: {finishReason})")}.");

            return ReadVerdict(text, model);
        }
    }

    // The model's reply is JSON in the form the request asked for. A verdict that is not one of the two words, or JSON that is not an object, is
    // as good as no reply, and asking again may get one.
    private Outcome ReadVerdict(string text, string model)
    {
        try
        {
            using var reply = JsonDocument.Parse(StripFence(text));
            var root = reply.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("verdict", out var verdictProperty)
                || verdictProperty.ValueKind != JsonValueKind.String)
                return Outcome.Transient("The AI service's reply held no verdict.");

            var verdict = verdictProperty.GetString()?.Trim().ToLower(CultureInfo.InvariantCulture) switch
            {
                "correct" => (TypedAnswerVerdict?)TypedAnswerVerdict.Correct,
                "wrong" => TypedAnswerVerdict.Wrong,
                _ => null,
            };
            if (verdict is null)
                return Outcome.Transient("The AI service's verdict was neither correct nor wrong.");

            var reason = root.TryGetProperty("reason", out var reasonProperty) && reasonProperty.ValueKind == JsonValueKind.String ? reasonProperty.GetString() : null;
            logger.LogDebug("The AI evaluator judged an answer {Verdict} ({Model}).", verdict, model);
            return Outcome.Done(new TextEvaluation(verdict.Value, reason, model));
        }
        catch (JsonException)
        {
            return Outcome.Transient("The AI service's reply was not JSON.");
        }
    }

    private Outcome Blocked(string? reason, string model)
    {
        logger.LogWarning("The AI service would not assess an answer ({Reason}); it is marked wrong, which staff can change by correcting the answer key.", reason ?? "no reason given");
        return Outcome.Done(new TextEvaluation(
            TypedAnswerVerdict.Wrong, $"The evaluation service would not assess this answer ({reason ?? "blocked"}), so it was marked wrong.", model));
    }

    private static string? ReplyText(JsonElement candidate)
    {
        if (!candidate.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Object
            || !content.TryGetProperty("parts", out var parts) || parts.ValueKind != JsonValueKind.Array)
            return null;

        var text = new StringBuilder();
        foreach (var part in parts.EnumerateArray())
        {
            // A model that thinks first marks those parts as its thoughts, which are not the reply.
            if (part.TryGetProperty("thought", out var thought) && thought.ValueKind == JsonValueKind.True)
                continue;
            if (part.TryGetProperty("text", out var partText) && partText.ValueKind == JsonValueKind.String)
                text.Append(partText.GetString());
        }

        return text.ToString();
    }

    // A model asked for JSON sometimes still wraps it in a code fence.
    private static string StripFence(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
            return trimmed;

        var firstLineEnd = trimmed.IndexOf('\n', StringComparison.Ordinal);
        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        return firstLineEnd >= 0 && lastFence > firstLineEnd ? trimmed[(firstLineEnd + 1)..lastFence].Trim() : trimmed;
    }

    private static (string? Status, string? Message, TimeSpan? RetryDelay) ReadError(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            if (!document.RootElement.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object)
                return (null, null, null);

            var status = error.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
            var message = error.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;

            // A rate limit may say how long to wait, in the service's own details, as "34s".
            TimeSpan? retryDelay = null;
            if (error.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in details.EnumerateArray())
                {
                    if (detail.ValueKind == JsonValueKind.Object
                        && detail.TryGetProperty("retryDelay", out var delay)
                        && delay.ValueKind == JsonValueKind.String
                        && delay.GetString() is { } text
                        && text.EndsWith('s')
                        && double.TryParse(text[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
                        && seconds >= 0)
                    {
                        retryDelay = TimeSpan.FromSeconds(seconds);
                        break;
                    }
                }
            }

            return (status, message, retryDelay);
        }
        catch (JsonException)
        {
            // Not JSON (a proxy's error page, say): there is nothing safe to repeat.
            return (null, null, null);
        }
    }

    // How long the service asked us to wait: its Retry-After header, in seconds or as a date, or else the delay in its error details.
    private static TimeSpan? RetryAfter(HttpResponseMessage response, TimeSpan? fromBody)
    {
        var header = response.Headers.RetryAfter;
        if (header?.Delta is { } delta && delta >= TimeSpan.Zero)
            return delta;
        if (header?.Date is { } date)
            return date > DateTimeOffset.UtcNow ? date - DateTimeOffset.UtcNow : TimeSpan.Zero;
        return fromBody;
    }

    private static string? Shorten(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Length <= 300 ? text.Trim() : text[..300].Trim();
}
