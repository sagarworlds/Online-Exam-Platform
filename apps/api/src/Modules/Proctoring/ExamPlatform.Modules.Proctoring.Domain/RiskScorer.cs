namespace ExamPlatform.Modules.Proctoring.Domain;

/// <summary>
/// Works out one attempt's risk score (FR-27). Pure: everything it needs is passed in, so the same facts and policy always give the same
/// score, and a reviewer can reproduce it by hand from the stored readings.
/// </summary>
public static class RiskScorer
{
    /// <summary>Scores one attempt.</summary>
    /// <param name="attempt">The attempt's facts.</param>
    /// <param name="sharedWrongAnswers">How many rare identical wrong answers it shares with one other candidate, from <see cref="SharedAnswerAnalyzer"/>.</param>
    /// <param name="policy">The rules to judge by.</param>
    /// <returns>The outcome, with a reading for every signal.</returns>
    public static RiskOutcome Score(AttemptRiskInputs attempt, int sharedWrongAnswers, RiskPolicy policy)
    {
        var readings = new List<RiskSignalReading>
        {
            RiskSignalReading.Judge(policy.RuleFor(RiskSignalKind.FocusDepartures), attempt.FocusDepartures),
            RiskSignalReading.Judge(policy.RuleFor(RiskSignalKind.ClientChanges), attempt.ClientChanges),
            RiskSignalReading.Judge(policy.RuleFor(RiskSignalKind.Invalidated), attempt.Invalidated ? 1 : 0),
            RiskSignalReading.Judge(policy.RuleFor(RiskSignalKind.FastCompletion), SecondsPerAnswer(attempt, policy)),
            RiskSignalReading.Judge(policy.RuleFor(RiskSignalKind.SharedWrongAnswers), sharedWrongAnswers),
        };

        var score = readings.Sum(r => r.Points);
        return new RiskOutcome(readings, score, policy.MaxScore, score >= policy.FlagThreshold);
    }

    /// <summary>
    /// The attempt's pace: seconds from start to submission, per answered question. The finish time is the submission, not the last
    /// answer saved, so time spent reviewing answers counts against the pace. That makes the signal err towards a slower pace, which
    /// means fewer people are flagged for thinking hard on the last few questions.
    /// </summary>
    /// <returns>Seconds per answer, or null when too few questions were answered to judge a pace.</returns>
    private static decimal? SecondsPerAnswer(AttemptRiskInputs attempt, RiskPolicy policy)
    {
        if (attempt.AnsweredCount < policy.MinAnswersForPace)
            return null;

        var seconds = (decimal)(attempt.FinishedAtUtc - attempt.StartedAtUtc).TotalSeconds;
        return seconds / attempt.AnsweredCount;
    }
}
