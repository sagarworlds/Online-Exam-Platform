namespace ExamPlatform.Modules.Invite.Endpoints.RateLimiting;

/// <summary>
/// Limits for the Invite module's named rate-limit policies (NFR-5), bound from <see cref="SectionName"/>. The defaults are per client
/// IP, per fixed window, and a starting point: a school whose classroom shares one public IP will need a higher one. They can be
/// generous because a direct sign-in code has 36^16 possibilities, so guessing is not what the limit is for; it stops the route being
/// used to hammer the database and the account lookup.
/// </summary>
public sealed class InviteRateLimitOptions
{
    /// <summary>The configuration section these options are bound from.</summary>
    public const string SectionName = "Invite:RateLimits";

    /// <summary>Limit for <see cref="InviteRateLimitPolicies.DirectSignIn"/>.</summary>
    public FixedWindowSettings DirectSignIn { get; set; } = new() { PermitLimit = 60, WindowSeconds = 5 * 60 };

    /// <summary>The name of the first policy whose settings are not positive, or null when all are valid.</summary>
    internal string? FindInvalidPolicy() => new (string Name, FixedWindowSettings Settings)[]
        {
            (nameof(DirectSignIn), DirectSignIn),
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
