namespace ExamPlatform.Api.RateLimiting;

/// <summary>
/// Settings for the global, per-client-IP fixed-window rate limiter applied to every
/// request (NFR-5), bound from <see cref="SectionName"/>. Configurable rather than
/// hard-coded so each environment can tune it — e.g. a school where a whole classroom
/// shares one public IP needs a higher limit than the defaults.
/// </summary>
public sealed class GlobalRateLimitOptions
{
    /// <summary>The configuration section these options are bound from.</summary>
    public const string SectionName = "RateLimiting:Global";

    /// <summary>How many requests one client IP may make per window. Must be positive.</summary>
    public int PermitLimit { get; set; } = 60;

    /// <summary>The length of the fixed window, in seconds. Must be positive.</summary>
    public int WindowSeconds { get; set; } = 60;
}
