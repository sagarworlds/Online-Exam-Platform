namespace ExamPlatform.Modules.Analytics.Application;

/// <summary>
/// The threshold the host configured for item analysis (FR-37): how many candidates must have had a question before its indices are shown.
/// The Endpoints project binds it from configuration, validates it at startup and passes it in, so the rule lives here and not in a config
/// reader.
/// </summary>
/// <param name="MinimumCohortSize">How many candidates must have had a question before its difficulty and discrimination are shown.</param>
public sealed record ItemAnalysisPolicy(int MinimumCohortSize);
