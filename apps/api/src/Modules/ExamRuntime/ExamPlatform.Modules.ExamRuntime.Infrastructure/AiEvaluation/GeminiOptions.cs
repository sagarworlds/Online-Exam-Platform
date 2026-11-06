using System.Text.RegularExpressions;

namespace ExamPlatform.Modules.ExamRuntime.Infrastructure.AiEvaluation;

/// <summary>
/// How the platform asks Google's Gemini service to judge typed answers, read from the <c>Gemini</c> configuration section. Typed answers are
/// judged by it only when a key is set: with no key, they are marked by the accepted answers alone, as before, and nothing waits.
/// </summary>
public sealed class GeminiOptions
{
    /// <summary>The configuration section these options are bound from.</summary>
    public const string SectionName = "Gemini";

    /// <summary>The longest a model name may be; it becomes part of a web address.</summary>
    public const int MaxModelLength = 100;

    /// <summary>The most tries one evaluation makes before it gives up for now.</summary>
    public const int MaxTriesLimit = 10;

    private static readonly Regex ModelName = new(@"^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// The API key from Google AI Studio. A secret: set it as <c>Gemini__ApiKey</c> in the host's environment, never in a file that is committed.
    /// It is sent in a header and never logged.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>Set false to switch the evaluator off while keeping the key. Typed answers are then marked by the accepted answers alone.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The model that judges. The default, <c>gemini-flash-latest</c>, is Google's name for its current Flash model, so a model Google retires
    /// does not stop the platform grading. Name one model (such as a dated stable release) to keep marking consistent over a long period.
    /// </summary>
    public string Model { get; set; } = "gemini-flash-latest";

    /// <summary>Where the service is. Only a test or a proxy changes it.</summary>
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com";

    /// <summary>The version of the service's interface.</summary>
    public string ApiVersion { get; set; } = "v1beta";

    /// <summary>
    /// How strictly the service's own safety filters may block an answer, as its <c>HARM_BLOCK_THRESHOLD</c> names it
    /// (<c>BLOCK_NONE</c>, <c>BLOCK_ONLY_HIGH</c>, <c>BLOCK_MEDIUM_AND_ABOVE</c>, <c>BLOCK_LOW_AND_ABOVE</c>). Blank sends no setting and leaves the
    /// service's own default. An exam on history or biology can quote text a strict filter blocks, and a blocked answer is marked wrong, so
    /// the default is lenient.
    /// </summary>
    public string? SafetyThreshold { get; set; } = "BLOCK_ONLY_HIGH";

    /// <summary>How long one request may take, in seconds, before it counts as a failure to be tried again.</summary>
    public int RequestTimeoutSeconds { get; set; } = 30;

    /// <summary>How many times one evaluation tries before it gives up for now. The answer is queued to be tried again later, so none is lost.</summary>
    public int MaxTries { get; set; } = 4;

    /// <summary>
    /// The most requests to send to the service in any minute, across every worker; 0 sends no limit of our own. Set it to the allowance
    /// Google shows for the key's project and model (AI Studio's usage page): staying under it is cheaper than being told to stop.
    /// </summary>
    public int RequestsPerMinute { get; set; }

    /// <summary>The wait before the second try, in seconds. Each later wait doubles, up to <see cref="RetryMaxDelaySeconds"/>.</summary>
    public double RetryBaseDelaySeconds { get; set; } = 1;

    /// <summary>The longest wait between two tries of one evaluation, in seconds.</summary>
    public double RetryMaxDelaySeconds { get; set; } = 20;

    /// <summary>Whether judging is on: switched on and a key is set.</summary>
    public bool IsEnabled => Enabled && !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>The model's name without a leading <c>models/</c>, which the service's own listings include and its address does not.</summary>
    public string ModelId => Model.Trim().StartsWith("models/", StringComparison.Ordinal) ? Model.Trim()[7..] : Model.Trim();

    /// <summary>Whether the settings can be used; <see cref="Problem"/> says what is wrong when they cannot.</summary>
    public bool IsValid => Problem is null;

    /// <summary>What is wrong with the settings, in a sentence an operator can act on, or null when nothing is.</summary>
    public string? Problem
    {
        get
        {
            if (!ModelName.IsMatch(ModelId) || ModelId.Length > MaxModelLength)
                return $"{SectionName}:Model must be a model name such as gemini-flash-latest: letters, digits, dots, dashes and underscores, at most {MaxModelLength}.";
            if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var address) || (address.Scheme != Uri.UriSchemeHttps && address.Scheme != Uri.UriSchemeHttp))
                return $"{SectionName}:BaseUrl must be a web address starting with https://.";
            if (string.IsNullOrWhiteSpace(ApiVersion) || ApiVersion.Trim('/').Contains('/'))
                return $"{SectionName}:ApiVersion must be a version such as v1beta.";
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
