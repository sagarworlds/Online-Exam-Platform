namespace ExamPlatform.Modules.Identity.Endpoints.RateLimiting;

/// <summary>
/// Limits for the Identity module's named rate-limit policies (NFR-5), bound from
/// <see cref="SectionName"/>. The defaults are per client IP, per fixed window, and are a
/// starting point: a school whose classroom shares one public IP will need higher ones.
/// </summary>
public sealed class IdentityRateLimitOptions
{
    /// <summary>The configuration section these options are bound from.</summary>
    public const string SectionName = "Identity:RateLimits";

    /// <summary>Limit for <see cref="IdentityRateLimitPolicies.OtpRequest"/>.</summary>
    public FixedWindowSettings OtpRequest { get; set; } = new() { PermitLimit = 20, WindowSeconds = 5 * 60 };

    /// <summary>Limit for <see cref="IdentityRateLimitPolicies.OtpVerify"/>.</summary>
    public FixedWindowSettings OtpVerify { get; set; } = new() { PermitLimit = 30, WindowSeconds = 5 * 60 };

    /// <summary>Limit for <see cref="IdentityRateLimitPolicies.PasswordLogin"/>.</summary>
    public FixedWindowSettings PasswordLogin { get; set; } = new() { PermitLimit = 10, WindowSeconds = 5 * 60 };

    /// <summary>Limit for <see cref="IdentityRateLimitPolicies.PasswordReset"/>.</summary>
    public FixedWindowSettings PasswordReset { get; set; } = new() { PermitLimit = 5, WindowSeconds = 15 * 60 };

    /// <summary>The name of the first policy whose settings are not positive, or null when all are valid.</summary>
    internal string? FindInvalidPolicy() => new (string Name, FixedWindowSettings Settings)[]
        {
            (nameof(OtpRequest), OtpRequest),
            (nameof(OtpVerify), OtpVerify),
            (nameof(PasswordLogin), PasswordLogin),
            (nameof(PasswordReset), PasswordReset),
        }
        .Where(policy => policy.Settings is not { PermitLimit: > 0, WindowSeconds: > 0 })
        .Select(policy => policy.Name)
        .FirstOrDefault();

    /// <summary>One fixed-window limit: how many requests a client IP may make per window.</summary>
    public sealed class FixedWindowSettings
    {
        /// <summary>How many requests one client IP may make per window. Must be positive.</summary>
        public int PermitLimit { get; set; }

        /// <summary>The length of the fixed window, in seconds. Must be positive.</summary>
        public int WindowSeconds { get; set; }
    }
}
