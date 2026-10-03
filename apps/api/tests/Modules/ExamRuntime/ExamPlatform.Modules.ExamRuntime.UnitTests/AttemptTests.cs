using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

public class AttemptTests
{
    private static readonly DateTime Start = Fixtures.Now;
    private static readonly DateTime Deadline = Start.AddMinutes(30);

    private static Attempt Open() => Attempt.Start(Guid.NewGuid(), Guid.NewGuid(), 1, Start, Deadline);

    [Fact]
    public void Start_BeginsAnInProgressAttemptWithNoAnswersOrScore()
    {
        var examId = Guid.NewGuid();
        var candidateId = Guid.NewGuid();

        var attempt = Attempt.Start(examId, candidateId, 1, Start, Deadline);

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
            () => Attempt.Start(Guid.NewGuid(), Guid.NewGuid(), 1, Start, Start.AddMinutes(minutesAfterStart)));

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
    public void ClearAnswer_RemovesTheChoice_SoTheQuestionCountsAsUnanswered()
    {
        var attempt = Open();
        var question = Guid.NewGuid();
        var other = Guid.NewGuid();
        attempt.RecordAnswer(question, Guid.NewGuid(), Start.AddMinutes(1));
        attempt.RecordAnswer(other, Guid.NewGuid(), Start.AddMinutes(1));

        attempt.ClearAnswer(question, Start.AddMinutes(2));

        Assert.Equal(other, Assert.Single(attempt.Answers).QuestionId);
    }

    [Fact]
    public void ClearAnswer_ForAQuestionWithNoAnswer_ChangesNothing()
    {
        var attempt = Open();

        attempt.ClearAnswer(Guid.NewGuid(), Start.AddMinutes(1));

        Assert.Empty(attempt.Answers);
    }

    [Fact]
    public void ClearAnswer_ThenAnswerAgain_StoresTheNewChoice()
    {
        var attempt = Open();
        var question = Guid.NewGuid();
        var second = Guid.NewGuid();
        attempt.RecordAnswer(question, Guid.NewGuid(), Start.AddMinutes(1));
        attempt.ClearAnswer(question, Start.AddMinutes(2));

        attempt.RecordAnswer(question, second, Start.AddMinutes(3));

        Assert.Equal(second, Assert.Single(attempt.Answers).SelectedOptionId);
    }

    [Fact]
    public void ClearAnswer_AtOrAfterTheDeadline_IsRefused_AndKeepsTheAnswer()
    {
        var attempt = Open();
        var question = Guid.NewGuid();
        attempt.RecordAnswer(question, Guid.NewGuid(), Start.AddMinutes(1));

        Assert.Throws<AttemptTimeExpiredError>(() => attempt.ClearAnswer(question, Deadline));

        Assert.Single(attempt.Answers);
    }

    [Fact]
    public void ClearAnswer_OnASubmittedAttempt_IsRefused_SoTheScoredAnswersCannotBeTakenBack()
    {
        var attempt = Open();
        var question = Guid.NewGuid();
        attempt.RecordAnswer(question, Guid.NewGuid(), Start.AddMinutes(1));
        attempt.Submit(Start.AddMinutes(5), 1, 2);

        Assert.Throws<AttemptNotInProgressError>(() => attempt.ClearAnswer(question, Start.AddMinutes(6)));

        Assert.Single(attempt.Answers);
    }

    [Fact]
    public void SetMarked_MarksTheQuestion_WithoutTouchingItsAnswer()
    {
        var attempt = Open();
        var question = Guid.NewGuid();
        var option = Guid.NewGuid();
        attempt.RecordAnswer(question, option, Start.AddMinutes(1));

        attempt.SetMarked(question, true, Start.AddMinutes(2));

        var mark = Assert.Single(attempt.Marks);
        Assert.Equal(question, mark.QuestionId);
        Assert.Equal(Start.AddMinutes(2), mark.MarkedAtUtc);
        Assert.Equal(option, Assert.Single(attempt.Answers).SelectedOptionId);
    }

    [Fact]
    public void SetMarked_AnUnansweredQuestion_IsAllowed()
    {
        var attempt = Open();

        attempt.SetMarked(Guid.NewGuid(), true, Start.AddMinutes(1));

        Assert.Single(attempt.Marks);
        Assert.Empty(attempt.Answers);
    }

    [Fact]
    public void SetMarked_Twice_KeepsOneMark()
    {
        var attempt = Open();
        var question = Guid.NewGuid();

        attempt.SetMarked(question, true, Start.AddMinutes(1));
        attempt.SetMarked(question, true, Start.AddMinutes(2));

        // The first mark stays as it was: marking a marked question is not a change.
        Assert.Equal(Start.AddMinutes(1), Assert.Single(attempt.Marks).MarkedAtUtc);
    }

    [Fact]
    public void SetMarked_False_TakesTheMarkOff_AndIsHarmlessWhenThereIsNone()
    {
        var attempt = Open();
        var marked = Guid.NewGuid();
        var kept = Guid.NewGuid();
        attempt.SetMarked(marked, true, Start.AddMinutes(1));
        attempt.SetMarked(kept, true, Start.AddMinutes(1));

        attempt.SetMarked(marked, false, Start.AddMinutes(2));
        attempt.SetMarked(Guid.NewGuid(), false, Start.AddMinutes(2));

        Assert.Equal(kept, Assert.Single(attempt.Marks).QuestionId);
    }

    [Fact]
    public void SetMarked_AtOrAfterTheDeadline_IsRefused()
    {
        var attempt = Open();

        Assert.Throws<AttemptTimeExpiredError>(() => attempt.SetMarked(Guid.NewGuid(), true, Deadline));

        Assert.Empty(attempt.Marks);
    }

    [Fact]
    public void SetMarked_OnASubmittedAttempt_IsRefused()
    {
        var attempt = Open();
        attempt.Submit(Start.AddMinutes(5), 0, 2);

        Assert.Throws<AttemptNotInProgressError>(() => attempt.SetMarked(Guid.NewGuid(), true, Start.AddMinutes(6)));
    }

    [Fact]
    public void Submit_KeepsTheScoreIndependentOfMarks()
    {
        var attempt = Open();
        attempt.SetMarked(Guid.NewGuid(), true, Start.AddMinutes(1));

        attempt.Submit(Start.AddMinutes(10), 3m, 5m);

        // A mark is only a note to the candidate; the caller scores from the answers alone.
        Assert.Equal(3m, attempt.Score);
        Assert.Single(attempt.Marks);
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

    [Fact]
    public void MoveToSection_StartsInTheFirstSection_AndOnlyMovesForward()
    {
        var attempt = Open();
        Assert.Equal(1, attempt.ActiveSectionOrder);

        attempt.MoveToSection(3, Start.AddMinutes(1));
        Assert.Equal(3, attempt.ActiveSectionOrder);

        attempt.MoveToSection(3, Start.AddMinutes(2));
        Assert.Equal(3, attempt.ActiveSectionOrder);

        Assert.Throws<SectionLockedError>(() => attempt.MoveToSection(2, Start.AddMinutes(3)));
        Assert.Equal(3, attempt.ActiveSectionOrder);
    }

    [Fact]
    public void MoveToSection_AfterTheDeadlineOrSubmit_IsRefused()
    {
        var late = Open();
        Assert.Throws<AttemptTimeExpiredError>(() => late.MoveToSection(2, Deadline));

        var done = Open();
        done.Submit(Start.AddMinutes(1), 0, 1);
        Assert.Throws<AttemptNotInProgressError>(() => done.MoveToSection(2, Start.AddMinutes(2)));
    }
}
