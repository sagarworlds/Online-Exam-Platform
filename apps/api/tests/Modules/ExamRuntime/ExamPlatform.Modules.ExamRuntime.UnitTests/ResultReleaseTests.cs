using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>The one rule for when a candidate may see which answers were right.</summary>
public class ResultReleaseTests
{
    private static readonly DateTime ReleaseAt = Fixtures.Now.AddDays(1);

    private static ExamSnapshot ExamWith(ExamResultReleaseMode mode, DateTime? at = null) =>
        Fixtures.Exam([Fixtures.Question()], resultRelease: mode, resultReleaseTime: at);

    [Fact]
    public void Instant_IsAlwaysReleased()
    {
        Assert.True(ResultRelease.IsReleased(ExamWith(ExamResultReleaseMode.Instant), Fixtures.Now));
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)] // the release instant itself counts as released
    [InlineData(1, true)]
    public void Scheduled_IsReleasedFromTheReleaseTime_Inclusive(int secondsFromReleaseTime, bool expected)
    {
        var exam = ExamWith(ExamResultReleaseMode.Scheduled, ReleaseAt);

        Assert.Equal(expected, ResultRelease.IsReleased(exam, ReleaseAt.AddSeconds(secondsFromReleaseTime)));
    }

    [Fact]
    public void Manual_IsHeldBackUntilAnAdministratorReleasesIt()
    {
        var held = ExamWith(ExamResultReleaseMode.Manual);
        var released = ExamWith(ExamResultReleaseMode.Manual, Fixtures.Now.AddHours(-1));

        Assert.False(ResultRelease.IsReleased(held, Fixtures.Now.AddYears(10)));
        Assert.True(ResultRelease.IsReleased(released, Fixtures.Now));
    }

    [Fact]
    public void Availability_SaysWhenAScheduledReleaseComes_AndNothingForAManualOne()
    {
        var scheduled = ResultRelease.AvailabilityOf(ExamWith(ExamResultReleaseMode.Scheduled, ReleaseAt), Fixtures.Now);
        var manual = ResultRelease.AvailabilityOf(ExamWith(ExamResultReleaseMode.Manual), Fixtures.Now);

        Assert.False(scheduled.Available);
        Assert.Equal(ExamResultReleaseMode.Scheduled, scheduled.Mode);
        Assert.Equal(ReleaseAt, scheduled.AvailableFromUtc);
        Assert.False(manual.Available);
        Assert.Equal(ExamResultReleaseMode.Manual, manual.Mode);
        Assert.Null(manual.AvailableFromUtc);
    }

    [Fact]
    public void Availability_OnceReleased_NoLongerNamesATime()
    {
        var released = ResultRelease.AvailabilityOf(ExamWith(ExamResultReleaseMode.Scheduled, ReleaseAt), ReleaseAt.AddMinutes(1));

        Assert.True(released.Available);
        Assert.Null(released.AvailableFromUtc);
    }
}
