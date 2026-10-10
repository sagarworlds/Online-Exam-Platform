namespace ExamPlatform.SharedKernel.Infrastructure.Observability;

/// <summary>
/// The number of days the platform's logs are declared to be kept (NFR-13, CERT-In). The application cannot keep them itself:
/// it writes to stdout, and the hosting's log store keeps them. This setting records what the store was configured for, and a
/// startup check refuses a value below <see cref="MinimumDays"/>, so a deployment cannot quietly declare less than the requirement.
/// </summary>
/// <remarks>
/// The floor is a constant and not configuration, so no deployment file can lower it. The value itself is configurable, because
/// the owner may keep logs longer than the minimum. Nothing in this application deletes logs early, and the setting does not
/// make the store keep anything: that is the owner's hosting decision (see docs/observability.md).
/// </remarks>
public sealed class LogRetentionOptions
{
    /// <summary>The configuration section, <c>LogRetention</c>.</summary>
    public const string SectionName = "LogRetention";

    /// <summary>
    /// 180 days: the retention NFR-13 of the requirements records as CERT-In-aligned. The check uses that figure as written there.
    /// It is not a reading of the CERT-In rules themselves, so counsel should confirm it (see docs/observability.md).
    /// </summary>
    public const int MinimumDays = 180;

    /// <summary>How many days the hosting's log store is configured to keep logs. Must be at least <see cref="MinimumDays"/>.</summary>
    public int Days { get; set; }

    /// <summary>The message the startup check reports when <see cref="Days"/> is below the minimum.</summary>
    public static string BelowMinimumMessage =>
        $"{SectionName}:Days must be at least {MinimumDays} (NFR-13, CERT-In). Set it to the number of days the log store keeps logs; it cannot be lower.";
}
