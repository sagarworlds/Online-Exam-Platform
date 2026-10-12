using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>The running total of time a candidate has spent on each question (FR-36): it only grows, and never outruns the sitting.</summary>
public class AttemptQuestionTimeTests
{
    private static readonly DateTime Start = Fixtures.Now;
    private static readonly DateTime Deadline = Start.AddMinutes(30);

    private static Attempt Open() => Attempt.Start(Guid.NewGuid(), Guid.NewGuid(), 1, Start, Deadline);

    [Fact]
    public void RecordQuestionTime_StoresTheTotal_ForAQuestionNotYetAnswered()
    {
        var attempt = Open();
        var question = Guid.NewGuid();

        attempt.RecordQuestionTime(question, 42, Start.AddMinutes(5));

        var time = Assert.Single(attempt.QuestionTimes);
        Assert.Equal(question, time.QuestionId);
        Assert.Equal(42, time.Seconds);
        Assert.Empty(attempt.Answers);
    }

    [Fact]
    public void RecordQuestionTime_RaisesTheTotal_WhenItGoesUp()
    {
        var attempt = Open();
        var question = Guid.NewGuid();
        attempt.RecordQuestionTime(question, 30, Start.AddMinutes(2));

        attempt.RecordQuestionTime(question, 75, Start.AddMinutes(3));

        Assert.Equal(75, Assert.Single(attempt.QuestionTimes).Seconds);
    }

    [Fact]
    public void RecordQuestionTime_ARepeatedOrLowerTotal_ChangesNothing_SoTheSameSecondsNeverCountTwice()
    {
        var attempt = Open();
        var question = Guid.NewGuid();
        attempt.RecordQuestionTime(question, 60, Start.AddMinutes(5));

        attempt.RecordQuestionTime(question, 60, Start.AddMinutes(5));
        attempt.RecordQuestionTime(question, 20, Start.AddMinutes(5));

        Assert.Equal(60, Assert.Single(attempt.QuestionTimes).Seconds);
    }

    [Fact]
    public void RecordQuestionTime_KeepsEachQuestionsTotalApart()
    {
        var attempt = Open();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        attempt.RecordQuestionTime(first, 10, Start.AddMinutes(5));
        attempt.RecordQuestionTime(second, 20, Start.AddMinutes(5));

        Assert.Equal(2, attempt.QuestionTimes.Count);
        Assert.Equal(10, attempt.QuestionTimes.Single(t => t.QuestionId == first).Seconds);
        Assert.Equal(20, attempt.QuestionTimes.Single(t => t.QuestionId == second).Seconds);
    }

    [Fact]
    public void RecordQuestionTime_IsCappedAtTheTimeSinceTheAttemptBegan()
    {
        var attempt = Open();
        var question = Guid.NewGuid();

        // Two minutes into the sitting nobody can have read one question for longer than 120 seconds, whatever the page claims.
        attempt.RecordQuestionTime(question, 9_999, Start.AddMinutes(2));

        Assert.Equal(120, Assert.Single(attempt.QuestionTimes).Seconds);
    }

    [Fact]
    public void RecordQuestionTime_ANegativeTotal_CountsAsNoneAndKeepsNoRow()
    {
        var attempt = Open();

        attempt.RecordQuestionTime(Guid.NewGuid(), -5, Start.AddMinutes(2));

        Assert.Empty(attempt.QuestionTimes);
    }

    [Fact]
    public void RecordQuestionTime_OnASubmittedAttempt_IsRefused()
    {
        var attempt = Open();
        attempt.Submit(Start.AddMinutes(6), 0m, 10m);

        Assert.Throws<AttemptNotInProgressError>(() => attempt.RecordQuestionTime(Guid.NewGuid(), 10, Start.AddMinutes(7)));
    }

    [Fact]
    public void RecordQuestionTime_AtOrAfterTheDeadline_IsRefused()
    {
        var attempt = Open();

        Assert.Throws<AttemptTimeExpiredError>(() => attempt.RecordQuestionTime(Guid.NewGuid(), 10, Deadline));
    }

    [Fact]
    public void RecordQuestionTime_WhenNotKept_StoresNothing()
    {
        var attempt = Open();

        attempt.RecordQuestionTime(Guid.NewGuid(), 42, Start.AddMinutes(2), keep: false);

        Assert.Empty(attempt.QuestionTimes);
    }

    [Fact]
    public void RecordQuestionTime_WhenNotKept_OnASubmittedAttempt_IsStillRefused()
    {
        var attempt = Open();
        attempt.Submit(Start.AddMinutes(6), 0m, 10m);

        Assert.Throws<AttemptNotInProgressError>(() => attempt.RecordQuestionTime(Guid.NewGuid(), 10, Start.AddMinutes(7), keep: false));
    }

    [Fact]
    public void RecordQuestionTime_WhilePaused_IsRefused_BecausePausedTimeIsNotReading()
    {
        var attempt = Open();
        attempt.Pause(Start.AddMinutes(1));

        Assert.Throws<AttemptPausedError>(() => attempt.RecordQuestionTime(Guid.NewGuid(), 10, Start.AddMinutes(2)));
    }
}
