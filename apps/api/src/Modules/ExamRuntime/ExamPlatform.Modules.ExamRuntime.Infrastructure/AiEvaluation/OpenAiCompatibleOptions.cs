using System.Text.RegularExpressions;

namespace ExamPlatform.Modules.ExamRuntime.Infrastructure.AiEvaluation;

/// <summary>
/// How the platform asks any service that speaks the OpenAI chat-completions protocol to judge typed answers, read from the
/// <c>OpenAiCompatible</c> configuration section. Groq, OpenRouter, Mistral, Cerebras, GitHub Models and a self-hosted Ollama or vLLM all do, so
/// which one is used is these settings and not code. The defaults are Groq's free tier. With no key the provider is simply off.
/// </summary>
public sealed class OpenAiCompatibleOptions
{
    /// <summary>The configuration section these options are bound from.</summary>
    public const string SectionName = "OpenAiCompatible";

    /// <summary>The longest a model name may be; it goes into the request.</summary>
    public const int MaxModelLength = 100;

    /// <summary>The most tries one evaluation makes before it gives up for now.</summary>
    public const int MaxTriesLimit = 10;

    // Letters, digits and . _ - : / so that "openai/gpt-oss-120b" and "meta-llama/llama-3.3-70b-instruct:free" are both names.
    private static readonly Regex ModelName = new(@"^[A-Za-z0-9][A-Za-z0-9._:/-]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// The provider's API key. A secret: set it as <c>OpenAiCompatible__ApiKey</c> in the host's environment, never in a file that is committed.
    /// It is sent in a header and never logged. A self-hosted server that needs none still needs a value here (any word) to be switched on.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>Set false to switch this provider off while keeping the key.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The address the protocol is served from, ending where <c>/chat/completions</c> begins. The default is Groq's.</summary>
    public string BaseUrl { get; set; } = "https://api.groq.com/openai/v1";

    /// <summary>
    /// The model that judges, as the provider names it. The default is the larger open-weight model Groq offers, which judges wording better than a
    /// small one. Groq retires models every few months (the earlier defaults, <c>llama-3.3-70b-versatile</c> and <c>llama-3.1-8b-instant</c>, are gone),
    /// and a retired one answers "model_not_found"; the models a key can use are listed by <c>GET {BaseUrl}/models</c>.
    /// </summary>
    public string Model { get; set; } = "openai/gpt-oss-120b";

    /// <summary>
    /// Further models to ask, comma-separated, when the first is out of quota or gone. Each model has its own allowance at most providers, so a
    /// smaller model with a bigger daily allowance (on Groq, <c>openai/gpt-oss-20b</c>) keeps judging when the larger one has reached its day.
    /// Empty asks the one model only.
    /// </summary>
    public string? FallbackModels { get; set; }

    /// <summary>
    /// Whether to ask for the reply as a JSON object (<c>response_format</c>). Leave on where the provider supports it; switch off for one that
    /// refuses the field, and the reply is then read from the first JSON object in the text.
    /// </summary>
    public bool JsonMode { get; set; } = true;

    /// <summary>
    /// The most requests to send to one model in any minute, across every worker; 0 sends no limit of our own. The default stays under Groq's
    /// free allowance of 30 a minute. Raise it on a paid plan, and set 0 for a server of your own.
    /// </summary>
    public int RequestsPerMinute { get; set; } = 25;

    /// <summary>How long one request may take, in seconds, before it counts as a failure to be tried again.</summary>
    public int RequestTimeoutSeconds { get; set; } = 30;

    /// <summary>How many times one evaluation tries before it gives up for now. The answer is queued to be tried again later, so none is lost.</summary>
    public int MaxTries { get; set; } = 3;

    /// <summary>The wait before the second try, in seconds. Each later wait doubles, up to <see cref="RetryMaxDelaySeconds"/>.</summary>
    public double RetryBaseDelaySeconds { get; set; } = 1;

    /// <summary>The longest wait between two tries of one evaluation, in seconds.</summary>
    public double RetryMaxDelaySeconds { get; set; } = 20;

    /// <summary>Whether judging is on: switched on and a key is set.</summary>
    public bool IsEnabled => Enabled && !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>The provider as staff see it, named by the host in <see cref="BaseUrl"/>.</summary>
    public string ProviderName =>
        Uri.TryCreate(BaseUrl, UriKind.Absolute, out var address) ? $"OpenAI-compatible ({address.Host})" : "OpenAI-compatible";

    /// <summary>Every model to ask, in the order to ask them: <see cref="Model"/>, then <see cref="FallbackModels"/>, each once.</summary>
    public IReadOnlyList<string> Models =>
        new[] { Model }.Concat((FallbackModels ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(model => model.Trim())
            .Where(model => model.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>Whether the settings can be used; <see cref="Problem"/> says what is wrong when they cannot.</summary>
    public bool IsValid => Problem is null;

    /// <summary>What is wrong with the settings, in a sentence an operator can act on, or null when nothing is.</summary>
    public string? Problem
    {
        get
        {
            foreach (var model in Models)
            {
                if (!ModelName.IsMatch(model) || model.Length > MaxModelLength)
                    return $"{SectionName}:Model and {SectionName}:FallbackModels must be model names such as openai/gpt-oss-120b: letters, digits and . _ - : /, at most {MaxModelLength} each.";
            }

            if (Models.Count == 0)
                return $"{SectionName}:Model must name a model.";
            if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var address) || (address.Scheme != Uri.UriSchemeHttps && address.Scheme != Uri.UriSchemeHttp))
                return $"{SectionName}:BaseUrl must be a web address starting with https://.";
            if (RequestTimeoutSeconds is < 1 or > 300)
                return $"{SectionName}:RequestTimeoutSeconds must be between 1 and 300.";
            if (MaxTries is < 1 or > MaxTriesLimit)
                return $"{SectionName}:MaxTries must be between 1 and {MaxTriesLimit}.";
            if (RequestsPerMinute is < 0 or > 100_000)
                return $"{SectionName}:RequestsPerMinute must be between 0 (no limit of our own) and 100000.";
            if (RetryBaseDelaySeconds is < 0 or > 60 || RetryMaxDelaySeconds is < 0 or > 300)
                return $"{SectionName}:RetryBaseDelaySeconds must be between 0 and 60 and {SectionName}:RetryMaxDelaySeconds between 0 and 300.";
            return null;
        }
    }
}
