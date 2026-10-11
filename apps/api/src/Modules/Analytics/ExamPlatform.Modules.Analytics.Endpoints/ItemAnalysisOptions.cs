namespace ExamPlatform.Modules.Analytics.Endpoints;

/// <summary>
/// The host's settings for item analysis (FR-37), bound from <c>Analytics:ItemAnalysis</c>. Kept in configuration so the threshold can be changed
/// for a pilot without a release.
/// </summary>
public sealed class ItemAnalysisOptions
{
    /// <summary>The configuration section these options are bound from.</summary>
    public const string SectionName = "Analytics:ItemAnalysis";

    /// <summary>The threshold used when the configuration sets none.</summary>
    /// <remarks>
    /// Thirty candidates is the usual floor for a discrimination index to be stable. Below it, the answers of one or two candidates can swing the
    /// figure from strongly positive to strongly negative, so a number that looks precise would mislead the person who reads it.
    /// </remarks>
    public const int DefaultMinimumCohortSize = 30;

    /// <summary>The lowest threshold accepted. Below ten, the groups hold one or two candidates each.</summary>
    public const int MinimumAllowedCohortSize = 10;

    /// <summary>How many candidates must have had a question before its difficulty and discrimination are shown.</summary>
    public int MinimumCohortSize { get; set; } = DefaultMinimumCohortSize;
}
