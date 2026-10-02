using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

public class AttemptTests
{
    private static readonly DateTime Start = Fixtures.Now;
    private static readonly DateTime Deadline = Start.AddMinutes(30);

    private static Attempt Open() => Attempt.Start(Guid.NewGuid(), Guid.NewGuid(), Start, Deadline);

    [Fact]
    public void Start_BeginsAnInProgressAttemptWithNoAnswersOrScore()
    {
        var examId = Guid.NewGuid();
        var candidateId = Guid.NewGuid();

        var attempt = Attempt.Start(examId, candidateId, Start, Deadline);

        Assert.Equal(AttemptStatus.InProgress, attempt.Status);
        Assert.Equal(examId, attempt.ExamId);
        Assert.Equal(candidateId, attempt.CandidateId);
        Assert.Equal(Deadline, attempt.DeadlineUtc);
        Assert.Empty(attempt.Answers);
        Assert.Null(attempt.Score);
        Assert.Null(attempt.SubmittedAtUtc);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Start_WithADeadlineThatIsNotAfterTheStart_IsRefused(int minutesAfterStart)
    {
        var error = Assert.Throws<InvalidAttemptError>(
            () => Attempt.Start(Guid.NewGuid(), Guid.NewGuid(), Start, Start.AddMinutes(minutesAfterStart)));

        Assert.Equal("invalid_attempt", error.ErrorCode);
    }

    [Fact]
    public void RecordAnswer_StoresTheChoice()
    {
        var attempt = Open();
        var question = Guid.NewGuid();
        var option = Guid.NewGuid();

        attempt.RecordAnswer(question, option, Start.AddMinutes(1));

        var answer = Assert.Single(attempt.Answers);
        Assert.Equal(question, answer.QuestionId);
        Assert.Equal(option, answer.SelectedOptionId);
    }

    [Fact]
    public void RecordAnswer_ForTheSameQuestionAgain_ReplacesTheChoiceInsteadOfAddingAnother()
    {
        var attempt = Open();
        var question = Guid.NewGuid();
        var second = Guid.NewGuid();
        attempt.RecordAnswer(question, Guid.NewGuid(), Start.AddMinutes(1));

        attempt.RecordAnswer(question, second, Start.AddMinutes(2));

        var answer = Assert.Single(attempt.Answers);
        Assert.Equal(second, answer.SelectedOptionId);
        Assert.Equal(Start.AddMinutes(2), answer.AnsweredAtUtc);
    }

    [Fact]
    public void RecordAnswer_AtOrAfterTheDeadline_IsRefused()
    {
        var attempt = Open();

        var error = Assert.Throws<AttemptTimeExpiredError>(() => attempt.RecordAnswer(Guid.NewGuid(), Guid.NewGuid(), Deadline));

        Assert.Equal("attempt_time_expired", error.ErrorCode);
        Assert.Equal(409, error.HttpStatusCode);
        Assert.Empty(attempt.Answers);
    }

    [Fact]
    public void RecordAnswer_OnASubmittedAttempt_IsRefused()
    {
        var attempt = Open();
        attempt.Submit(Start.AddMinutes(5), 1, 2);

        Assert.Throws<AttemptNotInProgressError>(() => attempt.RecordAnswer(Guid.NewGuid(), Guid.NewGuid(), Start.AddMinutes(6)));
    }

    [Fact]
    public void Submit_BeforeTheDeadline_RecordsTheScoreAndTheSubmissionTime()
    {
        var attempt = Open();

        attempt.Submit(Start.AddMinutes(10), 3.5m, 5m);

        Assert.Equal(AttemptStatus.Submitted, attempt.Status);
        Assert.Equal(3.5m, attempt.Score);
        Assert.Equal(5m, attempt.MaxScore);
        Assert.Equal(Start.AddMinutes(10), attempt.SubmittedAtUtc);
        Assert.False(attempt.AutoSubmitted);
    }

    [Fact]
    public void Submit_AfterTheDeadline_IsRecordedAsAnAutomaticSubmissionAtTheDeadline()
    {
        var attempt = Open();

        attempt.Submit(Deadline.AddHours(5), 1, 5);

        Assert.True(attempt.AutoSubmitted);
        Assert.Equal(Deadline, attempt.SubmittedAtUtc);
    }

    [Fact]
    public void Submit_Twice_IsRefused_AndKeepsTheFirstScore()
    {
        var attempt = Open();
        attempt.Submit(Start.AddMinutes(10), 2, 5);

        Assert.Throws<AttemptNotInProgressError>(() => attempt.Submit(Start.AddMinutes(11), 5, 5));

        Assert.Equal(2m, attempt.Score);
    }

    [Theory]
    [InlineData(29, false)]
    [InlineData(30, true)]
    [InlineData(31, true)]
    public void IsExpired_IsTrueFromTheDeadlineOnwards(int minutesAfterStart, bool expected) =>
        Assert.Equal(expected, Open().IsExpired(Start.AddMinutes(minutesAfterStart)));
}
