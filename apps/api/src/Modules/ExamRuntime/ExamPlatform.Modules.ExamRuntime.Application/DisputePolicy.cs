using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application.Dtos;
using ExamPlatform.Modules.ExamRuntime.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Application;

/// <summary>
/// How long after a result is released a candidate may dispute its answer key (FR-31, the "review and dispute window" between a published
/// and a final result). Set for the whole platform by <c>ExamRuntime:Disputes:WindowDays</c>; 0 switches disputes off. The window only
/// limits candidates: staff can still settle a dispute raised inside it, and correct an answer key at any time.
/// </summary>
/// <param name="WindowDays">How many days after the result is released a dispute may still be raised.</param>
public sealed record DisputePolicy(int WindowDays)
{
    /// <summary>The window used when none is configured.</summary>
    public const int DefaultWindowDays = 7;

    /// <summary>The longest window that may be configured: a result that can be disputed for longer than a year is not final in any useful sense.</summary>
    public const int MaxWindowDays = 365;

    /// <summary>Whether disputes are being taken at all.</summary>
    public bool Enabled => WindowDays > 0;

    /// <summary>Builds the policy from the configured number of days, falling back to the default when none is set.</summary>
    /// <param name="configuredDays">The configured value, or null when absent.</param>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or above <see cref="MaxWindowDays"/>: a typo to fix, not to guess at.</exception>
    public static DisputePolicy From(int? configuredDays)
    {
        var days = configuredDays ?? DefaultWindowDays;
        if (days is < 0 or > MaxWindowDays)
            throw new ArgumentOutOfRangeException(nameof(configuredDays), days, $"ExamRuntime:Disputes:WindowDays must be between 0 and {MaxWindowDays}.");

        return new DisputePolicy(days);
    }

    /// <summary>Where a candidate's result stands against the window.</summary>
    /// <param name="exam">The exam the attempt belongs to.</param>
    /// <param name="attempt">The submitted attempt.</param>
    /// <param name="nowUtc">The current instant.</param>
    public DisputeWindowDto WindowFor(ExamSnapshot exam, Attempt attempt, DateTime nowUtc)
    {
        if (!Enabled)
            return new DisputeWindowDto(false, false, null);

        var closesAt = ResultRelease.ReleasedAtUtc(exam, attempt)?.AddDays(WindowDays);
        return new DisputeWindowDto(true, closesAt is { } at && nowUtc < at, closesAt);
    }
}
