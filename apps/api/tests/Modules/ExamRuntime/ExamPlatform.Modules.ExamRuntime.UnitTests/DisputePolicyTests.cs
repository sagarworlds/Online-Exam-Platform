using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application;
using ExamPlatform.Modules.ExamRuntime.Domain;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>How long after a result is released a candidate may dispute its answer key (FR-31), and where that window starts.</summary>
public class DisputePolicyTests
{
    private static readonly DateTime SubmittedAt = Fixtures.Now.AddMinutes(-10);

    private static Attempt Submitted(Guid examId)
    {
        var attempt = Attempt.Start(examId, Guid.NewGuid(), 1, Fixtures.Now.AddMinutes(-40), Fixtures.Now.AddMinutes(30));
        attempt.Submit(SubmittedAt, 1, 1);
        return attempt;
    }

    private static ExamSnapshot ExamWith(ExamResultReleaseMode mode = ExamResultReleaseMode.Instant, DateTime? releaseAt = null) =>
        Fixtures.Exam([Fixtures.Question()], resultRelease: mode, resultReleaseTime: releaseAt);

    [Fact]
    public void WithNothingConfigured_TheWindowIsSevenDays()
    {
        Assert.Equal(7, DisputePolicy.From(null).WindowDays);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(365, true)]
    public void ZeroSwitchesDisputesOff_AndAnyLongerWindowUpToAYearTurnsThemOn(int days, bool enabled)
    {
        var policy = DisputePolicy.From(days);

        Assert.Equal(days, policy.WindowDays);
        Assert.Equal(enabled, policy.Enabled);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(366)]
    public void AWindowOutsideZeroToAYear_IsRefusedAsAMisconfiguration(int days)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DisputePolicy.From(days));
    }

    [Fact]
    public void WhenSwitchedOff_NothingCanBeDisputed_AndThereIsNoClosingTime()
    {
        var exam = ExamWith();

        var window = new DisputePolicy(0).WindowFor(exam, Submitted(exam.Id), Fixtures.Now);

        Assert.False(window.Enabled);
        Assert.False(window.Open);
        Assert.Null(window.ClosesAtUtc);
    }

    [Fact]
    public void AnInstantResult_CanBeDisputedForTheWindow_CountedFromTheSubmission()
    {
        var exam = ExamWith();
        var attempt = Submitted(exam.Id);
        var policy = new DisputePolicy(7);

        var closes = SubmittedAt.AddDays(7);
        Assert.Equal(closes, policy.WindowFor(exam, attempt, Fixtures.Now).ClosesAtUtc);
        Assert.True(policy.WindowFor(exam, attempt, closes.AddTicks(-1)).Open);
        Assert.False(policy.WindowFor(exam, attempt, closes).Open);
    }

    [Fact]
    public void AScheduledResult_IsCountedFromTheReleaseTime_NotFromTheSubmission()
    {
        var releaseAt = SubmittedAt.AddDays(3);
        var exam = ExamWith(ExamResultReleaseMode.Scheduled, releaseAt);

        var window = new DisputePolicy(7).WindowFor(exam, Submitted(exam.Id), releaseAt);

        Assert.Equal(releaseAt.AddDays(7), window.ClosesAtUtc);
        Assert.True(window.Open);
    }

    [Fact]
    public void AManualResult_IsCountedFromTheMomentItWasReleased()
    {
        var releasedAt = SubmittedAt.AddDays(1);
        var exam = ExamWith(ExamResultReleaseMode.Manual, releasedAt);

        Assert.Equal(releasedAt.AddDays(7), new DisputePolicy(7).WindowFor(exam, Submitted(exam.Id), releasedAt).ClosesAtUtc);
    }

    [Fact]
    public void AResultReleasedBeforeTheAttemptWasSubmitted_IsCountedFromTheSubmission()
    {
        // A scheduled release that came before this candidate finished: they could not see the answers until they submitted.
        var exam = ExamWith(ExamResultReleaseMode.Scheduled, SubmittedAt.AddHours(-5));

        Assert.Equal(SubmittedAt, ResultRelease.ReleasedAtUtc(exam, Submitted(exam.Id)));
    }

    [Fact]
    public void AManualResult_NobodyHasReleasedYet_HasNoWindow()
    {
        var exam = ExamWith(ExamResultReleaseMode.Manual);

        var window = new DisputePolicy(7).WindowFor(exam, Submitted(exam.Id), Fixtures.Now);

        Assert.Null(window.ClosesAtUtc);
        Assert.False(window.Open);
    }

    [Fact]
    public void AnAttemptStillOpen_HasNoReleaseTime()
    {
        var exam = ExamWith();
        var open = Attempt.Start(exam.Id, Guid.NewGuid(), 1, Fixtures.Now, Fixtures.Now.AddMinutes(30));

        Assert.Null(ResultRelease.ReleasedAtUtc(exam, open));
    }
}
