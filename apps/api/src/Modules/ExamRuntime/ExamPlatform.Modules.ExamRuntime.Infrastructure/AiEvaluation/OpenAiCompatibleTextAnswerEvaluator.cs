using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.SharedKernel.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamPlatform.Modules.ExamRuntime.Infrastructure.AiEvaluation;

/// <summary>
/// Judges typed answers with any service that speaks the OpenAI chat-completions protocol (Groq by default; also OpenRouter, Mistral, Cerebras,
/// GitHub Models, or a server of your own running Ollama or vLLM): one request per answer carrying the question, the accepted answers and the
/// candidate's answer, and a verdict coming back as JSON. One instance asks one model; a second model to fall back on is a second instance, because
/// a provider's allowance is mostly per model.
/// </summary>
/// <remarks>
/// <para>
/// It follows the same rules as <see cref="GeminiTextAnswerEvaluator"/> through the shared <see cref="CallLoop"/>: an overloaded or unreachable
/// service is tried again within the call; a refusal for going too fast or for a quota pauses the model for every worker for as long as it asked and
/// stops the call; a key it rejects or a model it does not have pauses it for ten minutes and is permanent. Groq says how long to wait in a
/// <c>retry-after</c> header, in its reset headers and in the words of the message ("try again in 1m26.4s"), and each is read.
/// </para>
/// <para>
/// The key travels in a header and nothing but the status and the service's own reason for a failure is logged: not the key, not the question,
/// not the key's answers and not what the candidate typed.
/// </para>
/// </remarks>
public sealed class OpenAiCompatibleTextAnswerEvaluator : IGatedTextAnswerEvaluator
{
    // Reasoning models put their thinking in the reply between these tags; it is not the verdict.
    private static readonly Regex Thinking = new(@"<think>.*?</think>", RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    // "2m59.56s", "7.66s", "120ms", "1h2m": the durations providers write in their messages and reset headers.
    private static readonly Regex DurationPart = new(@"(\d+(?:\.\d+)?)(ms|h|m|s)", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static readonly Regex TryAgainIn = new(@"try again in ((?:\d+(?:\.\d+)?(?:ms|h|m|s))+)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private readonly HttpClient _httpClient;
    private readonly IOptions<OpenAiCompatibleOptions> _options;
    private readonly string _model;
    private readonly ILogger<OpenAiCompatibleTextAnswerEvaluator> _logger;
    private readonly Func<TimeSpan, CancellationToken, Task> _wait;

    /// <summary>Creates the evaluator for one model.</summary>
    /// <param name="httpClient">The client that reaches the service. Its own time limit is off: this class applies one per try.</param>
    /// <param name="options">The <c>OpenAiCompatible</c> settings.</param>
    /// <param name="model">The model this instance asks.</param>
    /// <param name="logger">Where failures are reported, without any content.</param>
    /// <param name="wait">How to wait between tries; the real delay unless a test replaces it.</param>
    /// <param name="gate">The pacing and pause shared by every worker; one is made when none is given (for a test).</param>
    public OpenAiCompatibleTextAnswerEvaluator(
        HttpClient httpClient,
        IOptions<OpenAiCompatibleOptions> options,
        string model,
        ILogger<OpenAiCompatibleTextAnswerEvaluator> logger,
        Func<TimeSpan, CancellationToken, Task>? wait = null,
        EvaluatorGate? gate = null)
    {
        _httpClient = httpClient;
        _options = options;
        _model = model;
        _logger = logger;
        _wait = wait ?? ((delay, token) => Task.Delay(delay, token));
        Gate = gate ?? new EvaluatorGate(options.Value.ProviderName, model, options.Value.RequestsPerMinute, new SystemClock(), logger);
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
            throw new InvalidOperationException($"The AI evaluator is not set up: {OpenAiCompatibleOptions.SectionName}:ApiKey is empty or {OpenAiCompatibleOptions.SectionName}:Enabled is false.");

        var address = new Uri($"{settings.BaseUrl.TrimEnd('/')}/chat/completions");
        var body = BuildBody(request, settings);

        var plan = new CallPlan(Gate, settings.MaxTries, settings.RetryBaseDelaySeconds, settings.RetryMaxDelaySeconds, _wait, _logger);
        return await CallLoop.RunAsync(plan, token => TryOnceAsync(address, body, settings, token), cancellationToken);
    }

    private async Task<Outcome> TryOnceAsync(Uri address, string body, OpenAiCompatibleOptions settings, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, address) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        // In a header, never in the address: nothing that names the address, such as a log line or an exception message, can then carry the key.
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.RequestTimeoutSeconds));

        try
        {
            using var response = await _httpClient.SendAsync(message, timeout.Token);
            var content = await response.Content.ReadAsStringAsync(timeout.Token);
            return response.IsSuccessStatusCode ? ReadReply(content) : ReadFailure(response, content);
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

    private string BuildBody(TextEvaluationRequest request, OpenAiCompatibleOptions settings)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = _model,
            // The same answer is marked the same way each time as far as the service allows.
            ["temperature"] = 0,
            ["messages"] = new object[]
            {
                new { role = "system", content = TextEvaluationPrompt.SystemInstruction },
                new { role = "user", content = TextEvaluationPrompt.Build(request) },
            },
        };

        if (settings.JsonMode)
            payload["response_format"] = new { type = "json_object" };

        return JsonSerializer.Serialize(payload);
    }

    private Outcome ReadFailure(HttpResponseMessage response, string content)
    {
        var code = (int)response.StatusCode;
        var error = ReadError(content);
        var summary = $"The AI service answered {code}{(error.Code is null ? string.Empty : $" ({error.Code})")}.";
        // The provider's own reason names a limit, a model or a setting and never the request's content, so it can be kept and shown to an operator.
        var detail = $"{code} {error.Code ?? error.Type ?? "unknown"}: {Shorten(error.Message) ?? "no reason given"}";

        if (code == 429)
        {
            var named = NamedWait(response, error.Message);
            return error.QuotaExhausted
                ? Outcome.Failed(FailureKind.QuotaExhausted, summary, named, detail)
                : Outcome.Failed(FailureKind.RateLimited, summary, named, detail);
        }

        // 408 and 409 are the service asking to be tried again, and 5xx (including Groq's 498 and 503 for no capacity) is its own trouble.
        if (code is 408 or 409 or 498 or >= 500)
            return Outcome.Failed(FailureKind.Overloaded, summary, NamedWait(response, error.Message), detail);

        // Every other refusal will be refused again until something is changed. A key it rejects, a model it does not have, one that has been
        // retired, or one that does not take the reply format asked for are about the setup, so the whole model is left alone; anything else
        // is about this one request.
        var aboutTheSetup = code is 401 or 403 or 404 || (code == 400 && IsAboutTheSetup(error));
        _logger.LogError(
            "The AI evaluator was refused (status {StatusCode} {ErrorCode}): {Reason}. Asking again will not help until this is changed.",
            code, error.Code ?? error.Type ?? "unknown", Shorten(error.Message) ?? "no reason given");
        return Outcome.Failed(aboutTheSetup ? FailureKind.Refused : FailureKind.RefusedRequest, summary, null, detail);
    }

    private static bool IsAboutTheSetup(OpenAiError error) =>
        (error.Code?.Contains("model", StringComparison.OrdinalIgnoreCase) ?? false)
        || (error.Code?.Contains("api_key", StringComparison.OrdinalIgnoreCase) ?? false)
        || (error.Message?.Contains("model", StringComparison.OrdinalIgnoreCase) ?? false)
        || (error.Message?.Contains("response_format", StringComparison.OrdinalIgnoreCase) ?? false)
        || (error.Message?.Contains("api key", StringComparison.OrdinalIgnoreCase) ?? false);

    private Outcome ReadReply(string content)
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
            var model = root.TryGetProperty("model", out var named) && named.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(named.GetString())
                ? named.GetString()!
                : _model;

            if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
                return Outcome.Failed(FailureKind.BadReply, "The AI service's answer held no reply.");

            var choice = choices[0];
            var finishReason = choice.TryGetProperty("finish_reason", out var finish) && finish.ValueKind == JsonValueKind.String ? finish.GetString() : null;
            if (string.Equals(finishReason, "content_filter", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("The AI service would not assess an answer ({Reason}); it is marked wrong, which staff can change by correcting the answer key.", finishReason);
                return Outcome.Done(new TextEvaluation(
                    Domain.TypedAnswerVerdict.Wrong, "The evaluation service would not assess this answer (content_filter), so it was marked wrong.", model));
            }

            var text = choice.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object
                && message.TryGetProperty("content", out var contentProperty) && contentProperty.ValueKind == JsonValueKind.String
                    ? Thinking.Replace(contentProperty.GetString() ?? string.Empty, string.Empty)
                    : null;
            if (string.IsNullOrWhiteSpace(text))
                return Outcome.Failed(FailureKind.BadReply, $"The AI service's answer held no text{(finishReason is null ? string.Empty : $" (it stopped: {finishReason})")}.");

            var read = VerdictReader.Read(text, model);
            if (read.Evaluation is not { } evaluation)
                return Outcome.Failed(FailureKind.BadReply, read.Problem!);

            _logger.LogDebug("The AI evaluator judged an answer {Verdict} ({Model}).", evaluation.Verdict, model);
            return Outcome.Done(evaluation);
        }
    }

    private readonly record struct OpenAiError(string? Message, string? Type, string? Code, bool QuotaExhausted);

    private static OpenAiError ReadError(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            if (!document.RootElement.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object)
                return new OpenAiError(null, null, null, false);

            string? Text(string name) => error.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            var message = Text("message");
            var type = Text("type");
            var code = Text("code");

            // Groq names the limit in the message: "... on requests per day (RPD): Limit 1000, Used 1000 ...". A day's allowance is a quota, which a
            // minute's wait does not cure; a limit per minute is only being asked too fast.
            var daily = (message?.Contains("per day", StringComparison.OrdinalIgnoreCase) ?? false)
                || (message?.Contains("(RPD)", StringComparison.Ordinal) ?? false)
                || (message?.Contains("(TPD)", StringComparison.Ordinal) ?? false)
                || (code?.Contains("insufficient_quota", StringComparison.OrdinalIgnoreCase) ?? false)
                || (type?.Contains("insufficient_quota", StringComparison.OrdinalIgnoreCase) ?? false);
            return new OpenAiError(message, type, code, daily);
        }
        catch (JsonException)
        {
            // Not JSON (a proxy's error page, say): there is nothing safe to repeat.
            return new OpenAiError(null, null, null, false);
        }
    }

    // How long the service asked us to wait: its Retry-After header, in seconds or as a date; else the words of its message ("try again in 14s");
    // else the longer of its reset headers.
    private static TimeSpan? NamedWait(HttpResponseMessage response, string? message)
    {
        var header = response.Headers.RetryAfter;
        if (header?.Delta is { } delta && delta >= TimeSpan.Zero)
            return delta;
        if (header?.Date is { } date)
            return date > DateTimeOffset.UtcNow ? date - DateTimeOffset.UtcNow : TimeSpan.Zero;

        if (message is not null && TryAgainIn.Match(message) is { Success: true } match && ParseDuration(match.Groups[1].Value) is { } fromMessage)
            return fromMessage;

        TimeSpan? longest = null;
        foreach (var name in new[] { "x-ratelimit-reset-requests", "x-ratelimit-reset-tokens" })
        {
            if (response.Headers.TryGetValues(name, out var values) && ParseDuration(values.FirstOrDefault()) is { } reset && (longest is null || reset > longest))
                longest = reset;
        }

        return longest;
    }

    /// <summary>Reads a duration written as providers write it ("2m59.56s", "7.66s", "120ms", "1h2m").</summary>
    /// <param name="text">The text.</param>
    /// <returns>The duration, or null when the text holds none.</returns>
    public static TimeSpan? ParseDuration(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var total = TimeSpan.Zero;
        var any = false;
        foreach (Match part in DurationPart.Matches(text))
        {
            if (!double.TryParse(part.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                continue;

            any = true;
            total += part.Groups[2].Value switch
            {
                "h" => TimeSpan.FromHours(number),
                "m" => TimeSpan.FromMinutes(number),
                "s" => TimeSpan.FromSeconds(number),
                _ => TimeSpan.FromMilliseconds(number),
            };
        }

        return any ? total : null;
    }

    private static string? Shorten(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Length <= 300 ? text.Trim() : text[..300].Trim();
}
