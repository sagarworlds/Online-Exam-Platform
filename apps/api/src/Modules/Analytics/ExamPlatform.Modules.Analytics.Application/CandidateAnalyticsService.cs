using ExamPlatform.Modules.Analytics.Contracts;
using ExamPlatform.Modules.Analytics.Domain;
using ExamPlatform.Modules.ExamRuntime.Contracts;

namespace ExamPlatform.Modules.Analytics.Application;

/// <summary>
/// Builds a candidate's analytics (FR-36) from the released results that ExamRuntime exposes. It reads results through
/// <see cref="ICandidateResultReader"/> only, so it never sees a result the candidate could not open, and it keeps no data of its own.
/// </summary>
public sealed class CandidateAnalyticsService(ICandidateResultReader results) : ICandidateAnalytics
{
    /// <inheritdoc />
    public async Task<CandidateAnalyticsDto> GetAsync(Guid candidateId, CancellationToken cancellationToken)
    {
        var released = await results.ListReleasedAsync(candidateId, cancellationToken);

        // The trend is in the order the candidate sat the exams, so an improvement or a slump reads left to right.
        var trend = released
            .OrderBy(r => r.SubmittedAtUtc)
            .Select(r => new ScorePointDto(
                r.AttemptId,
                r.ExamId,
                r.ExamName,
                r.SubmittedAtUtc,
                r.Score,
                r.MaxScore,
                PerformanceMeasures.PercentOfMarks(r.Score, r.MaxScore)))
            .ToList();

        return new CandidateAnalyticsDto(released.Count, trend, SectionsOf(released));
    }

    /// <summary>
    /// Adds up the sections by name across the results, ignoring case and surrounding spaces, so "Physics" in two exams is one subject.
    /// </summary>
    /// <remarks>
    /// Sorted weakest accuracy first, so the sections to work on are at the top. A section nobody answered has no accuracy and sorts last,
    /// because it says nothing about where the candidate is weak.
    /// </remarks>
    private static IReadOnlyList<SectionPerformanceDto> SectionsOf(IReadOnlyList<CandidateResult> released) =>
        released
            .SelectMany(r => r.Sections)
            .GroupBy(s => s.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var tally = group.Aggregate(
                    AnswerTally.Empty,
                    (total, s) => total.Add(AnswerTally.Of(s.CorrectCount, s.WrongCount, s.PartialCount, s.UnansweredCount)));

                return new SectionPerformanceDto(
                    group.Key,
                    group.Count(),
                    tally.Correct,
                    tally.Wrong,
                    tally.Partial,
                    tally.Unanswered,
                    tally.Accuracy);
            })
            .OrderBy(s => s.Accuracy ?? decimal.MaxValue)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
