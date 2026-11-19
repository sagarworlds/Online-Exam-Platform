using ExamPlatform.Modules.ExamAuthoring.Application.Commands;
using ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;

namespace ExamPlatform.Modules.ExamAuthoring.UnitTests;

/// <summary>How the minutes an author sends with a schedule (FR-13) become the seconds an exam stores.</summary>
public class ScheduleDurationTests
{
    [Fact]
    public void NoDuration_StaysNoDuration() => Assert.Null(ScheduleExamCommand.SecondsFromMinutes(null));

    [Theory]
    [InlineData(1, 60)]
    [InlineData(90, 5400)]
    [InlineData(35791394, 2147483640)]     // the most minutes whose seconds still fit in a 32-bit integer
    public void Minutes_BecomeSeconds(int minutes, int seconds) => Assert.Equal(seconds, ScheduleExamCommand.SecondsFromMinutes(minutes));

    [Theory]
    [InlineData(35791395)]                 // one minute more than fits
    [InlineData(int.MaxValue)]
    public void ADurationTooLongToCount_IsRefusedAsAnInvalidConfig_NotAnOverflow(int minutes)
    {
        var error = Assert.Throws<InvalidExamConfigError>(() => ScheduleExamCommand.SecondsFromMinutes(minutes));

        Assert.Equal("invalid_exam_config", error.ErrorCode);
        Assert.Equal(400, error.HttpStatusCode);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, -60)]
    [InlineData(-35791394, -2147483640)]
    [InlineData(-35791395, int.MinValue)]  // too far below zero to count: held at the smallest whole number, still not a positive duration
    [InlineData(int.MinValue, int.MinValue)]
    public void ANonPositiveDuration_StaysNonPositive_ForTheExamToRefuse(int minutes, int seconds) =>
        Assert.Equal(seconds, ScheduleExamCommand.SecondsFromMinutes(minutes));
}
