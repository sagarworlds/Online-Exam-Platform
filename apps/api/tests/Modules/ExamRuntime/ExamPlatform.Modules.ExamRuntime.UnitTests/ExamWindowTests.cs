using ExamPlatform.Modules.ExamRuntime.Application;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

public class ExamWindowTests
{
    private static readonly DateTime Start = Fixtures.Now;

    [Fact]
    public void CanStart_IsTrueFromTheStartUntilTheEnd()
    {
        var exam = Fixtures.Exam([], start: Start, end: Start.AddHours(2));

        Assert.False(ExamWindow.CanStart(exam, Start.AddSeconds(-1)));
        Assert.True(ExamWindow.CanStart(exam, Start));
        Assert.True(ExamWindow.CanStart(exam, Start.AddHours(2)));
        Assert.False(ExamWindow.CanStart(exam, Start.AddHours(2).AddSeconds(1)));
    }

    [Fact]
    public void CanStart_StopsAtTheLateEntryDeadlineWhenThereIsOne()
    {
        var exam = Fixtures.Exam([], start: Start, end: Start.AddHours(2), lateEntry: Start.AddMinutes(15));

        Assert.True(ExamWindow.CanStart(exam, Start.AddMinutes(15)));
        Assert.False(ExamWindow.CanStart(exam, Start.AddMinutes(16)));
    }

    [Fact]
    public void DeadlineUtc_IsTheDurationAfterTheStartOfTheAttempt()
    {
        var exam = Fixtures.Exam([], start: Start, end: Start.AddHours(3), durationSeconds: 3600);

        Assert.Equal(Start.AddMinutes(30).AddHours(1), ExamWindow.DeadlineUtc(exam, Start.AddMinutes(30)));
    }

    [Fact]
    public void DeadlineUtc_NeverRunsPastTheEndOfTheWindow()
    {
        var exam = Fixtures.Exam([], start: Start, end: Start.AddHours(2), durationSeconds: 3600);

        Assert.Equal(Start.AddHours(2), ExamWindow.DeadlineUtc(exam, Start.AddMinutes(90)));
    }

    [Fact]
    public void DeadlineUtc_WithoutADuration_IsTheEndOfTheWindow()
    {
        var exam = Fixtures.Exam([], start: Start, end: Start.AddHours(2), durationSeconds: null);

        Assert.Equal(Start.AddHours(2), ExamWindow.DeadlineUtc(exam, Start.AddMinutes(10)));
    }
}
