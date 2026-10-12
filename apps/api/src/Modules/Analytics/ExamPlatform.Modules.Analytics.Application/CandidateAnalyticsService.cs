using ExamPlatform.Modules.Analytics.Contracts;
using ExamPlatform.Modules.Analytics.Domain;
using ExamPlatform.Modules.ExamRuntime.Contracts;
using ExamPlatform.Modules.QuestionBank.Contracts;

namespace ExamPlatform.Modules.Analytics.Application;

/// <summary>
/// Builds a candidate's analytics (FR-36) from the released results that ExamRuntime exposes. It reads results through
/// <see cref="ICandidateResultReader"/> only, so it never sees a result the candidate could not open, and it keeps no data of its own. The
/// topic labels come from the question bank through <see cref="IQuestionTopics"/>, which holds labels and never a score or an answer key.
/// </summary>
public sealed class CandidateAnalyticsService(ICandidateResultReader results, IQuestionTopics topics) : ICandidateAnalytics
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

        return new CandidateAnalyticsDto(released.Count, trend, SectionsOf(released), TopicsOf(released, labels), TimeOf(released));
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
    private static TimePerQuestionDto TimeOf(IReadOnlyList<CandidateResult> released)
    {
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
