namespace ExamPlatform.Modules.Analytics.Contracts;

/// <summary>Whether a candidate is shown an exam's topper and average (FR-36).</summary>
/// <remarks>
/// A status rather than a zero: a held-back topper or average must not look like a result of zero, and a candidate must be able to tell "not enough
/// candidates yet" from "the topper scored nothing".
/// </remarks>
public enum ExamComparisonStatus
{
    /// <summary>The cohort met the minimum, so the topper and the average are shown.</summary>
    Shown,

    /// <summary>
    /// The cohort is below the minimum, so the topper, the average and the cohort's size are withheld. The candidate's own percentage is still shown.
    /// </summary>
    BelowMinimum,
}
