namespace ExamPlatform.Modules.Analytics.Contracts;

/// <summary>
/// How a candidate's best result at one exam stands against that exam's cohort (FR-36): the topper and the average of the cohort, next to the
/// candidate's own percentage. One entry per exam the candidate has a released result for, oldest first.
/// </summary>
/// <remarks>
/// Why one entry per exam rather than one per result: the cohort is per exam, and a candidate who sat an exam twice is compared on the best result,
/// which is the result the cohort counts. Putting the figures on each trend point would repeat them for every sitting.
/// Why the figures carry no identity: the output is figures only, never another candidate's id, name or result, so a candidate cannot work out how
/// someone else did. The only exam fields are the candidate's own exam's id and name.
/// Why a cohort below the minimum shows no size, topper or average: see <see cref="ExamComparisonStatus.BelowMinimum"/>.
/// </remarks>
/// <param name="ExamId">The exam.</param>
/// <param name="ExamName">The exam's name.</param>
/// <param name="CandidatePercentOfMarks">The candidate's best counted result at the exam as a share of its marks, in percent; null when its paper had no marks.</param>
/// <param name="Status">Whether the topper and the average are shown, and if not, why.</param>
/// <param name="MinimumCohortSize">How many candidates must have a result at the exam before its topper and average are shown.</param>
/// <param name="CohortSize">How many candidates' results the topper and average were worked from; null while the cohort is below the minimum.</param>
/// <param name="TopperPercentOfMarks">The highest percentage of marks in the cohort, in percent; null unless the status is Shown.</param>
/// <param name="AveragePercentOfMarks">The mean percentage of marks in the cohort, to two decimals; null unless the status is Shown.</param>
public sealed record ExamComparisonDto(
    Guid ExamId,
    string ExamName,
    decimal? CandidatePercentOfMarks,
    ExamComparisonStatus Status,
    int MinimumCohortSize,
    int? CohortSize,
    decimal? TopperPercentOfMarks,
    decimal? AveragePercentOfMarks);
