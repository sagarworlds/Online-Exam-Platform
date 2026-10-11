using ExamPlatform.Modules.Proctoring.Domain;

namespace ExamPlatform.Modules.Proctoring.UnitTests;

/// <summary>The risk score: every signal judged against its rule, the points added up, and the flag set at the threshold (FR-27).</summary>
public class RiskScorerTests
{
    private static RiskSignalReading ReadingOf(RiskOutcome outcome, RiskSignalKind kind) =>
        outcome.Readings.Single(r => r.Kind == kind);

    [Fact]
    public void ACleanAttempt_ScoresNothing_AndIsNotFlagged()
    {
        var outcome = RiskScorer.Score(RiskTestData.Attempt(), 0, RiskTestData.DefaultPolicy);

        Assert.Equal(0, outcome.Score);
        Assert.False(outcome.Flagged);
        Assert.Equal(5, outcome.Readings.Count);
        Assert.All(outcome.Readings, r => Assert.False(r.Raised));
    }

    [Fact]
    public void ReadingsAreInTheOrderOfTheSignalKinds()
    {
        var outcome = RiskScorer.Score(RiskTestData.Attempt(), 0, RiskTestData.DefaultPolicy);

        Assert.Equal(Enum.GetValues<RiskSignalKind>(), outcome.Readings.Select(r => r.Kind));
    }

    [Fact]
    public void ThreeDeparturesFromTheExamPage_RaiseTheFocusSignal_AndFlagOnItsOwn()
    {
        // The focus signal alone is worth the flag threshold, so three departures put the attempt in the queue by themselves.
        var outcome = RiskScorer.Score(RiskTestData.Attempt(focus: 3), 0, RiskTestData.DefaultPolicy);

        var reading = ReadingOf(outcome, RiskSignalKind.FocusDepartures);
        Assert.True(reading.Raised);
        Assert.Equal(30, reading.Points);
        Assert.Equal(30, outcome.Score);
        Assert.True(outcome.Flagged);
    }

    [Fact]
    public void TwoDeparturesAreBelowTheirThreshold_AndAddNothing()
    {
        var outcome = RiskScorer.Score(RiskTestData.Attempt(focus: 2), 0, RiskTestData.DefaultPolicy);

        var reading = ReadingOf(outcome, RiskSignalKind.FocusDepartures);
        Assert.False(reading.Raised);
        Assert.Equal(0, reading.Points);
        Assert.False(outcome.Flagged);
    }

    [Fact]
    public void WeakSignalsTogether_ReachTheThreshold_WhenNoOneSignalDoes()
    {
        // Two address changes (10) and an invalidation (15) are 25: below the threshold of 30. A fast pace (10) takes them to 35.
        var withoutPace = RiskScorer.Score(RiskTestData.Attempt(changes: 2, invalidated: true), 0, RiskTestData.DefaultPolicy);
        var withPace = RiskScorer.Score(RiskTestData.Attempt(changes: 2, invalidated: true, answered: 20, elapsedSeconds: 60), 0, RiskTestData.DefaultPolicy);

        Assert.Equal(25, withoutPace.Score);
        Assert.False(withoutPace.Flagged);
        Assert.Equal(35, withPace.Score);
        Assert.True(withPace.Flagged);
    }

    [Fact]
    public void AnInvalidatedResult_CountsAsARaisedSignal()
    {
        var outcome = RiskScorer.Score(RiskTestData.Attempt(invalidated: true), 0, RiskTestData.DefaultPolicy);

        var reading = ReadingOf(outcome, RiskSignalKind.Invalidated);
        Assert.True(reading.Raised);
        Assert.Equal(1, reading.Value);
        Assert.Equal(15, reading.Points);
    }

    [Fact]
    public void APaceFasterThanTheThreshold_RaisesTheCompletionSignal()
    {
        // Twenty answers in a hundred seconds is five seconds a question: at the threshold, which counts (the signal is "at most").
        var outcome = RiskScorer.Score(RiskTestData.Attempt(answered: 20, elapsedSeconds: 100), 0, RiskTestData.DefaultPolicy);

        var reading = ReadingOf(outcome, RiskSignalKind.FastCompletion);
        Assert.True(reading.Raised);
        Assert.Equal(5m, reading.Value);
        Assert.Equal(10, reading.Points);
    }

    [Fact]
    public void ASlowerPace_IsNotRaised()
    {
        // Twenty answers over six hundred seconds is thirty seconds a question.
        var outcome = RiskScorer.Score(RiskTestData.Attempt(answered: 20, elapsedSeconds: 600), 0, RiskTestData.DefaultPolicy);

        var reading = ReadingOf(outcome, RiskSignalKind.FastCompletion);
        Assert.False(reading.Raised);
        Assert.Equal(30m, reading.Value);
    }

    [Fact]
    public void APaceIsNotJudged_WhenTooFewQuestionsWereAnswered()
    {
        // Five answers in twenty seconds looks fast, but the pace of a candidate who answered five questions says nothing.
        var outcome = RiskScorer.Score(RiskTestData.Attempt(answered: 5, elapsedSeconds: 20), 0, RiskTestData.DefaultPolicy);

        var reading = ReadingOf(outcome, RiskSignalKind.FastCompletion);
        Assert.Null(reading.Value);
        Assert.False(reading.Raised);
        Assert.Equal(0, reading.Points);
    }

    [Fact]
    public void SharedWrongAnswers_AreScoredWithTheirOwnRule()
    {
        var outcome = RiskScorer.Score(RiskTestData.Attempt(), 3, RiskTestData.DefaultPolicy);

        var reading = ReadingOf(outcome, RiskSignalKind.SharedWrongAnswers);
        Assert.Equal(3, reading.Value);
        Assert.True(reading.Raised);
        Assert.Equal(35, reading.Points);
        Assert.True(outcome.Flagged);
    }

    [Fact]
    public void MaxScore_IsTheSumOfEveryWeight()
    {
        var outcome = RiskScorer.Score(RiskTestData.Attempt(), 0, RiskTestData.DefaultPolicy);

        Assert.Equal(100, outcome.MaxScore);
    }

    [Fact]
    public void Scoring_IsRepeatable_FromTheSameFacts()
    {
        var attempt = RiskTestData.Attempt(focus: 4, changes: 1, answered: 12, elapsedSeconds: 90);

        var first = RiskScorer.Score(attempt, 1, RiskTestData.DefaultPolicy);
        var second = RiskScorer.Score(attempt, 1, RiskTestData.DefaultPolicy);

        Assert.Equal(first.Score, second.Score);
        Assert.Equal(first.Readings, second.Readings);
    }
}
