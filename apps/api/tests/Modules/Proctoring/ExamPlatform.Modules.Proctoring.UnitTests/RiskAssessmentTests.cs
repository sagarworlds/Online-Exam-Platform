using ExamPlatform.Modules.Proctoring.Domain;
using ExamPlatform.Modules.Proctoring.Domain.Exceptions;

namespace ExamPlatform.Modules.Proctoring.UnitTests;

/// <summary>The assessment's life: scored, then decided once by a reviewer, with the decision kept as it was (FR-27).</summary>
public class RiskAssessmentTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Reviewer = Guid.NewGuid();

    private static RiskAssessment FlaggedAssessment(out AttemptRiskInputs input)
    {
        input = RiskTestData.Attempt(focus: 3);
        return RiskAssessment.Create(input.ExamId, input, RiskScorer.Score(input, 0, RiskTestData.DefaultPolicy), Now);
    }

    private static RiskAssessment UnflaggedAssessment(out AttemptRiskInputs input)
    {
        input = RiskTestData.Attempt();
        return RiskAssessment.Create(input.ExamId, input, RiskScorer.Score(input, 0, RiskTestData.DefaultPolicy), Now);
    }

    [Fact]
    public void Create_OpensTheAssessment_WithItsScoreAndEverySignal()
    {
        var assessment = FlaggedAssessment(out var input);

        Assert.Equal(RiskFlagStatus.Open, assessment.Status);
        Assert.True(assessment.Flagged);
        Assert.Equal(30, assessment.Score);
        Assert.Equal(100, assessment.MaxScore);
        Assert.Equal(input.AttemptId, assessment.AttemptId);
        Assert.Equal(5, assessment.Signals.Count);
        Assert.Equal(Now, assessment.ComputedAtUtc);
        Assert.Null(assessment.DecidedAtUtc);
    }

    [Fact]
    public void MarkReviewed_RecordsTheReviewerTheTimeAndATrimmedNote()
    {
        var assessment = FlaggedAssessment(out _);

        assessment.MarkReviewed(Reviewer, "  Looked at the departures; a phone call. ", Now.AddMinutes(5));

        Assert.Equal(RiskFlagStatus.Reviewed, assessment.Status);
        Assert.Equal(Reviewer, assessment.DecidedByUserId);
        Assert.Equal(Now.AddMinutes(5), assessment.DecidedAtUtc);
        Assert.Equal("Looked at the departures; a phone call.", assessment.DecisionNote);
    }

    [Fact]
    public void MarkReviewed_NeedsNoNote()
    {
        var assessment = FlaggedAssessment(out _);

        assessment.MarkReviewed(Reviewer, null, Now);

        Assert.Equal(RiskFlagStatus.Reviewed, assessment.Status);
        Assert.Null(assessment.DecisionNote);
    }

    [Fact]
    public void Dismiss_NeedsANote_AndLeavesTheFlagOpenWithoutOne()
    {
        var assessment = FlaggedAssessment(out _);

        Assert.Throws<InvalidRiskDecisionError>(() => assessment.Dismiss(Reviewer, "   ", Now));
        Assert.Throws<InvalidRiskDecisionError>(() => assessment.Dismiss(Reviewer, null, Now));

        Assert.Equal(RiskFlagStatus.Open, assessment.Status);
    }

    [Fact]
    public void Dismiss_RecordsTheNote()
    {
        var assessment = FlaggedAssessment(out _);

        assessment.Dismiss(Reviewer, "Exam switched to a shared tablet at the centre; confirmed by the invigilator.", Now);

        Assert.Equal(RiskFlagStatus.Dismissed, assessment.Status);
        Assert.Equal("Exam switched to a shared tablet at the centre; confirmed by the invigilator.", assessment.DecisionNote);
    }

    [Fact]
    public void Dismiss_RefusesANoteLongerThanTheLimit()
    {
        var assessment = FlaggedAssessment(out _);

        Assert.Throws<InvalidRiskDecisionError>(() => assessment.Dismiss(Reviewer, new string('x', RiskAssessment.MaxNoteLength + 1), Now));
    }

    [Fact]
    public void AnUnflaggedAttempt_CannotBeReviewedOrDismissed()
    {
        var assessment = UnflaggedAssessment(out _);

        Assert.Throws<RiskFlagNotRaisedError>(() => assessment.MarkReviewed(Reviewer, null, Now));
        Assert.Throws<RiskFlagNotRaisedError>(() => assessment.Dismiss(Reviewer, "No reason needed here", Now));
    }

    [Fact]
    public void ADecidedFlag_CannotBeDecidedAgain()
    {
        var assessment = FlaggedAssessment(out _);
        assessment.MarkReviewed(Reviewer, null, Now);

        Assert.Throws<RiskFlagAlreadyDecidedError>(() => assessment.Dismiss(Reviewer, "Changed my mind", Now));
        Assert.Equal(RiskFlagStatus.Reviewed, assessment.Status);
    }

    [Fact]
    public void Rescore_ReplacesAnOpenScore()
    {
        var assessment = UnflaggedAssessment(out var input);
        var later = input with { FocusDepartures = 3 };

        assessment.Rescore(later, RiskScorer.Score(later, 0, RiskTestData.DefaultPolicy), Now.AddHours(1));

        Assert.True(assessment.Flagged);
        Assert.Equal(30, assessment.Score);
        Assert.Equal(Now.AddHours(1), assessment.ComputedAtUtc);
    }

    [Fact]
    public void Rescore_LeavesADecisionAsItWas()
    {
        var assessment = FlaggedAssessment(out var input);
        assessment.Dismiss(Reviewer, "Checked the recording of the session", Now);
        var later = input with { FocusDepartures = 9 };

        Assert.Throws<RiskFlagAlreadyDecidedError>(() => assessment.Rescore(later, RiskScorer.Score(later, 0, RiskTestData.DefaultPolicy), Now.AddHours(1)));
        Assert.Equal(30, assessment.Score);
    }
}
