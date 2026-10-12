using ExamPlatform.Modules.Analytics.Contracts;
using ExamPlatform.Modules.Analytics.Domain;
using ExamPlatform.Modules.ExamRuntime.Contracts;
using ExamPlatform.Modules.QuestionBank.Contracts;

namespace ExamPlatform.Modules.Analytics.Application;

/// <summary>
/// Builds a candidate's analytics (FR-36) from the released results that ExamRuntime exposes. It reads the candidate's own results through
/// <see cref="ICandidateResultReader"/> only, so it never sees a result the candidate could not open, and it keeps no data of its own. The
/// topic labels come from the question bank through <see cref="IQuestionTopics"/>, which holds labels and never a score or an answer key.
/// The comparison with the topper and the average reads each exam's cohort through <see cref="IExamResponseReader"/>, the port item analysis
/// (FR-37) uses, and the threshold comes from the same <see cref="ItemAnalysisPolicy"/> so the two rules agree on the minimum cohort size.
/// </summary>
public sealed class CandidateAnalyticsService(
    ICandidateResultReader results,
    IQuestionTopics topics,
    IExamResponseReader responses,
    ItemAnalysisPolicy policy) : ICandidateAnalytics
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

        var labels = await topics.GetTopicsAsync(
            released.SelectMany(r => r.Questions ?? []).Select(q => q.QuestionId).Distinct().ToList(),
            cancellationToken);

        return new CandidateAnalyticsDto(
            released.Count,
            trend,
            SectionsOf(released),
            TopicsOf(released, labels),
            TimeOf(released),
            await ComparisonsOfAsync(candidateId, released, cancellationToken));
    }

    /// <summary>
    /// Compares the candidate's best result at each exam they sat with that exam's cohort (FR-36): the topper and the average, shown only when the
    /// cohort meets the minimum. The candidate's own percentage is always shown, so they see where they stand.
    /// </summary>
    /// <remarks>
    /// Why only released results: the cohort comes from <see cref="IExamResponseReader"/>, which applies the exam's release rule, so an exam whose
    /// results are held has no attempts and contributes nothing. The candidate's own results come from the same released list as the trend, so an
    /// unreleased result of theirs is never compared either. The rule is needed because a held score can still change (a key dispute, FR-31), and
    /// showing it to another candidate's comparison would reveal a result the author has not released.
    /// Why the candidate's own result is in the cohort without being added separately: the reader counts every candidate who sat the exam, the
    /// candidate included, so their best result is already one of the figures the topper and average are worked from.
    /// Why one exam at a time: the reader shares this request's database context, so the cohorts are read in turn.
    /// </remarks>
    private async Task<IReadOnlyList<ExamComparisonDto>> ComparisonsOfAsync(
        Guid candidateId, IReadOnlyList<CandidateResult> released, CancellationToken cancellationToken)
    {
        var comparisons = new List<ExamComparisonDto>();

        // Oldest first, by the candidate's first submission at the exam, so the comparisons read in the order the exams were sat.
        foreach (var sittings in released.GroupBy(r => r.ExamId).OrderBy(group => group.Min(r => r.SubmittedAtUtc)))
        {
            var read = await responses.ReadAsync(sittings.Key, cancellationToken);
            var cohort = CohortRule.Of(CohortOf(read), policy.MinimumCohortSize);
            var own = CohortRule.BestOf(sittings.Select(r => new CohortAttempt(candidateId, r.Score, r.MaxScore, r.SubmittedAtUtc)));

            comparisons.Add(new ExamComparisonDto(
                sittings.Key,
                sittings.First().ExamName,
                own is { } best ? CohortRule.PercentOf(best) : null,
                StatusOf(cohort.Status),
                policy.MinimumCohortSize,
                cohort.Size,
                cohort.TopperPercent,
                cohort.AveragePercent));
        }

        return comparisons;
    }

    /// <summary>
    /// The counted results of an exam's cohort, as the cohort rule reads them. A held exam, or one the reader cannot find, has no cohort, so it
    /// contributes nothing rather than a guess.
    /// </summary>
    private static IEnumerable<CohortAttempt> CohortOf(ExamResponses? read) =>
        read is { ResultsReleased: true }
            ? read.Attempts.Select(a => new CohortAttempt(a.CandidateId, a.Score, a.MaxScore, a.SubmittedAtUtc))
            : Enumerable.Empty<CohortAttempt>();

    private static ExamComparisonStatus StatusOf(CohortStatus status) => status switch
    {
        CohortStatus.Shown => ExamComparisonStatus.Shown,
        CohortStatus.BelowMinimum => ExamComparisonStatus.BelowMinimum,
        _ => throw new InvalidOperationException($"Unknown cohort status {status}."),
    };

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

    /// <summary>Works out accuracy by topic, using each question's labels as the question bank holds them now.</summary>
    private static IReadOnlyList<TopicPerformanceDto> TopicsOf(
        IReadOnlyList<CandidateResult> released, IReadOnlyDictionary<Guid, IReadOnlyList<string>> labels) =>
        TopicPerformance.Of(
                released
                    .SelectMany(r => r.Questions ?? [])
                    .Select(q => (Topics: LabelsOf(labels, q.QuestionId), Answer: TallyOf(q.Outcome))))
            .Select(t => new TopicPerformanceDto(
                t.Name,
                t.QuestionCount,
                t.Answers.Correct,
                t.Answers.Wrong,
                t.Answers.Partial,
                t.Answers.Unanswered,
                t.Answers.Accuracy))
            .ToList();

    /// <summary>
    /// Works out the time per question, overall and by section name. Only questions the page reported on are timed; see <see cref="QuestionTiming"/>.
    /// </summary>
    /// <remarks>
    /// When no released result carries a time (none is released, or every one has its time withheld for a candidate under 18 while the switch is
    /// off), there is no figure to show and none is returned: null, not a count of zero. Otherwise the figures come from the results that kept their
    /// times, and a withheld result carries none anyway.
    /// </remarks>
    private static TimePerQuestionDto? TimeOf(IReadOnlyList<CandidateResult> released)
    {
        if (released.All(r => r.TimeWithheld))
            return null;

        var timed = released
            .SelectMany(r => (r.Questions ?? [])
                .Where(q => q.TimeSpentSeconds is not null)
                .Select(q => (Section: SectionNameOf(r, q.SectionId), Seconds: q.TimeSpentSeconds!.Value)))
            .ToList();

        var sections = timed
            .GroupBy(t => t.Section, StringComparer.OrdinalIgnoreCase)
            .Select(group => new SectionTimeDto(
                group.Key,
                group.Count(),
                QuestionTiming.AverageOf(group.Select(t => t.Seconds).ToList())))
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new TimePerQuestionDto(
            timed.Count,
            QuestionTiming.AverageOf(timed.Select(t => t.Seconds).ToList()),
            sections);
    }

    private static IReadOnlyList<string> LabelsOf(IReadOnlyDictionary<Guid, IReadOnlyList<string>> labels, Guid questionId) =>
        labels.TryGetValue(questionId, out var found) ? found : [];

    // The section a question sat in, by its name, for grouping time by section. A result always lists the section of each of its questions, so the
    // empty name is never shown in practice; it only keeps a malformed result from failing the whole analytics page.
    private static string SectionNameOf(CandidateResult result, Guid sectionId) =>
        result.Sections.FirstOrDefault(s => s.SectionId == sectionId)?.Name.Trim() ?? string.Empty;

    // One question's answer as the tally the domain adds up: exactly one of the four counts is 1.
    private static AnswerTally TallyOf(CandidateQuestionOutcome outcome) => outcome switch
    {
        CandidateQuestionOutcome.Correct => AnswerTally.Of(1, 0, 0, 0),
        CandidateQuestionOutcome.Partial => AnswerTally.Of(0, 0, 1, 0),
        CandidateQuestionOutcome.Wrong => AnswerTally.Of(0, 1, 0, 0),
        CandidateQuestionOutcome.Unanswered => AnswerTally.Of(0, 0, 0, 1),
        _ => throw new InvalidOperationException($"Unknown question outcome {outcome}."),
    };
}
