using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.SharedKernel.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamPlatform.Modules.ExamRuntime.Infrastructure.AiEvaluation;

/// <summary>
/// Judges typed answers with Google's Gemini service: one request per answer carrying the question, the accepted answers and the candidate's
/// answer, and a verdict, right or wrong, coming back as JSON in a form the service is told to keep to.
/// </summary>
/// <remarks>
/// <para>
/// What is worth trying again within one call is a service that is overloaded or unreachable (408, 409, 5xx, no connection, no answer in time) and
/// a reply with no usable verdict: up to <see cref="GeminiOptions.MaxTries"/> times with a doubling wait. A refusal for going too fast or for a
/// quota (429) is not: asking again at once only adds failed requests, so the call stops, the shared <see cref="EvaluatorGate"/> pauses the service
/// for every worker for as long as it asked (and for a day's quota, much longer), and the caller schedules the answer for after the pause. A refusal
/// that waiting cannot cure (a key the service rejects, a model it does not have) pauses it for ten minutes and is marked permanent.
/// </para>
/// <para>
/// One reply is a verdict of its own, so it is not retried: the service blocked the answer for its safety rules. It is marked wrong, with
/// that said as the reason, because asking the same words again is blocked the same way and an exam cannot wait for ever for it.
/// </para>
/// <para>
/// The key travels in a header and nothing but the status and the service's own reason for a failure is logged: not the key, not the question,
/// not the key's answers and not what the candidate typed.
/// </para>
/// </remarks>
public sealed class GeminiTextAnswerEvaluator : IGatedTextAnswerEvaluator
{
    // The four categories the service's safety setting names. Each is given the same threshold.
    private static readonly string[] HarmCategories =
    [
        "HARM_CATEGORY_HARASSMENT", "HARM_CATEGORY_HATE_SPEECH", "HARM_CATEGORY_SEXUALLY_EXPLICIT", "HARM_CATEGORY_DANGEROUS_CONTENT",
    ];

    // Why the service may give no answer at all, which asking again does not change.
    private static readonly HashSet<string> BlockedFinishReasons =
        new(StringComparer.OrdinalIgnoreCase) { "SAFETY", "PROHIBITED_CONTENT", "BLOCKLIST", "SPII", "RECITATION", "IMAGE_SAFETY" };

    private readonly HttpClient _httpClient;
    private readonly IOptions<GeminiOptions> _options;
    private readonly ILogger<GeminiTextAnswerEvaluator> _logger;
    private readonly Func<TimeSpan, CancellationToken, Task> _wait;

    /// <summary>Creates the evaluator.</summary>
    /// <param name="httpClient">The client that reaches the service. Its own time limit is off: this class applies one per try.</param>
    /// <param name="options">The <c>Gemini</c> settings.</param>
    /// <param name="logger">Where failures are reported, without any content.</param>
    /// <param name="wait">How to wait between tries; the real delay unless a test replaces it.</param>
    /// <param name="gate">The pacing and pause shared by every worker; one is made when none is given (for a test).</param>
    public GeminiTextAnswerEvaluator(
        HttpClient httpClient,
        IOptions<GeminiOptions> options,
        ILogger<GeminiTextAnswerEvaluator> logger,
        Func<TimeSpan, CancellationToken, Task>? wait = null,
        EvaluatorGate? gate = null)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
        _wait = wait ?? ((delay, token) => Task.Delay(delay, token));
        Gate = gate ?? new EvaluatorGate("Gemini", options.Value.ModelId, options.Value.RequestsPerMinute, new SystemClock(), logger);
    }

    /// <inheritdoc />
    public EvaluatorGate Gate { get; }

    /// <inheritdoc />
    public bool IsEnabled => _options.Value.IsEnabled;

    /// <inheritdoc />
    public async Task<TextEvaluation> EvaluateAsync(TextEvaluationRequest request, CancellationToken cancellationToken)
    {
        var settings = _options.Value;
        if (!settings.IsEnabled)
            throw new InvalidOperationException($"The AI evaluator is not set up: {GeminiOptions.SectionName}:ApiKey is empty or {GeminiOptions.SectionName}:Enabled is false.");

        var address = new Uri($"{settings.BaseUrl.TrimEnd('/')}/{settings.ApiVersion.Trim('/')}/models/{settings.ModelId}:generateContent");
        var body = BuildBody(request, settings);

        var plan = new CallPlan(Gate, settings.MaxTries, settings.RetryBaseDelaySeconds, settings.RetryMaxDelaySeconds, _wait, _logger);
        return await CallLoop.RunAsync(plan, token => TryOnceAsync(address, body, settings, token), cancellationToken);
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
            using var response = await _httpClient.SendAsync(message, timeout.Token);
            var content = await response.Content.ReadAsStringAsync(timeout.Token);
            return response.IsSuccessStatusCode ? ReadReply(content, settings) : ReadFailure(response, content);
        }
        catch (HttpRequestException)
        {
            return Outcome.Failed(FailureKind.Overloaded, "The AI service could not be reached.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The request's own time limit, as distinct from the caller asking to stop.
            return Outcome.Failed(FailureKind.Overloaded, "The AI service did not answer in time.");
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
        var error = ReadError(content);
        var summary = $"The AI service answered {code}{(error.Status is null ? string.Empty : $" ({error.Status})")}.";
        // Google's own reason names a quota, a model or a setting and never the request's content, so it can be kept and shown to an operator.
        var detail = $"{code} {error.Status ?? "unknown"}: {Shorten(error.Message) ?? "no reason given"}";
        var retryAfter = RetryAfter(response, error.RetryDelay);

        // 429 is a limit: a short one (too fast) or a quota, which is mostly the day's and may be zero for this key and model.
        if (code == 429)
        {
            // A day's quota does not come back in the seconds its retry delay names, so only a limit that is not the day's passes its wait on.
            return error.QuotaExhausted
                ? Outcome.Failed(FailureKind.QuotaExhausted, summary, null, detail)
                : Outcome.Failed(FailureKind.RateLimited, summary, retryAfter, detail);
        }

        // 408 and 409 are the service asking to be tried again, and 5xx is its own trouble.
        if (code is 408 or 409 or >= 500)
            return Outcome.Failed(FailureKind.Overloaded, summary, retryAfter, detail);

        // Every other refusal will be refused again until something is changed. A key it rejects, a project or place it will not serve and a
        // model it does not have are about the setup, so the whole service is left alone; anything else is about this one request.
        var aboutTheSetup = code is 401 or 403 or 404 || (code == 400 && IsAboutTheSetup(error));
        _logger.LogError(
            "The AI evaluator was refused (status {StatusCode} {ErrorStatus}): {Reason}. Asking again will not help until this is changed.",
            code, error.Status ?? "unknown", Shorten(error.Message) ?? "no reason given");
        return Outcome.Failed(aboutTheSetup ? FailureKind.Refused : FailureKind.RefusedRequest, summary, null, detail);
    }

    // Google reports an invalid key as a 400, and a place it does not serve as a failed precondition.
    private static bool IsAboutTheSetup(GoogleError error) =>
        string.Equals(error.Status, "FAILED_PRECONDITION", StringComparison.OrdinalIgnoreCase)
        || (error.Message?.Contains("API key", StringComparison.OrdinalIgnoreCase) ?? false);

    private Outcome ReadReply(string content, GeminiOptions settings)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(content);
        }
        catch (JsonException)
        {
            return Outcome.Failed(FailureKind.BadReply, "The AI service's answer could not be read.");
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
                return Outcome.Failed(FailureKind.BadReply, "The AI service's answer held no reply.");

            var candidate = candidates[0];
            var finishReason = candidate.TryGetProperty("finishReason", out var finish) && finish.ValueKind == JsonValueKind.String ? finish.GetString() : null;
            if (finishReason is not null && BlockedFinishReasons.Contains(finishReason))
                return Blocked(finishReason, model);

            var text = ReplyText(candidate);
            if (string.IsNullOrWhiteSpace(text))
                return Outcome.Failed(FailureKind.BadReply, $"The AI service's answer held no text{(finishReason is null ? string.Empty : $" (it stopped: {finishReason})")}.");

            var read = VerdictReader.Read(text, model);
            if (read.Evaluation is not { } evaluation)
                return Outcome.Failed(FailureKind.BadReply, read.Problem!);

            _logger.LogDebug("The AI evaluator judged an answer {Verdict} ({Model}).", evaluation.Verdict, model);
            return Outcome.Done(evaluation);
        }
    }

    private Outcome Blocked(string? reason, string model)
    {
        _logger.LogWarning("The AI service would not assess an answer ({Reason}); it is marked wrong, which staff can change by correcting the answer key.", reason ?? "no reason given");
        return Outcome.Done(new TextEvaluation(
            Domain.TypedAnswerVerdict.Wrong, $"The evaluation service would not assess this answer ({reason ?? "blocked"}), so it was marked wrong.", model));
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

    private readonly record struct GoogleError(string? Status, string? Message, TimeSpan? RetryDelay, bool QuotaExhausted);

    private static GoogleError ReadError(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            if (!document.RootElement.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object)
                return new GoogleError(null, null, null, false);

            var status = error.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
            var message = error.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;

            // A rate limit may say how long to wait, in the service's own details, as "34s", and which quota was passed.
            TimeSpan? retryDelay = null;
            var perDay = false;
            if (error.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in details.EnumerateArray())
                {
                    if (detail.ValueKind != JsonValueKind.Object)
                        continue;

                    retryDelay ??= ReadRetryDelay(detail);
                    perDay |= NamesADailyQuota(detail);
                }
            }

            // "limit: 0" means this key and model have no allowance at all, which no amount of waiting changes.
            var zero = message?.Contains("limit: 0", StringComparison.OrdinalIgnoreCase) ?? false;
            var daily = message?.Contains("per day", StringComparison.OrdinalIgnoreCase) ?? false;
            return new GoogleError(status, message, retryDelay, perDay || zero || daily);
        }
        catch (JsonException)
        {
            // Not JSON (a proxy's error page, say): there is nothing safe to repeat.
            return new GoogleError(null, null, null, false);
        }
    }

    private static TimeSpan? ReadRetryDelay(JsonElement detail) =>
        detail.TryGetProperty("retryDelay", out var delay)
        && delay.ValueKind == JsonValueKind.String
        && delay.GetString() is { } text
        && text.EndsWith('s')
        && double.TryParse(text[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
        && seconds >= 0
            ? TimeSpan.FromSeconds(seconds)
            : null;

    // A quota failure lists each quota that was passed with its id, such as "GenerateRequestsPerDayPerProjectPerModel-FreeTier".
    private static bool NamesADailyQuota(JsonElement detail)
    {
        if (!detail.TryGetProperty("violations", out var violations) || violations.ValueKind != JsonValueKind.Array)
            return false;

        foreach (var violation in violations.EnumerateArray())
        {
            if (violation.ValueKind != JsonValueKind.Object)
                continue;

            foreach (var field in new[] { "quotaId", "quotaMetric" })
            {
                if (violation.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String
                    && value.GetString() is { } id && id.Contains("PerDay", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
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
