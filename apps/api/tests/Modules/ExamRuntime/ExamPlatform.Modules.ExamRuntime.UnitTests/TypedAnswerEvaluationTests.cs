using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Events;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>
/// How a typed answer is marked once the AI evaluator has a say: an answer that matches an accepted answer is right without it, any other
/// is right only if it was judged right, and an answer with no verdict is judged by the accepted answers alone, as it always was.
/// </summary>
public class TypedAnswerVerdictScoringTests
{
    private static readonly QuestionSnapshot Capital = Fixtures.TextQuestion("Paris");
    private static readonly ExamSnapshot Exam = Fixtures.Exam([Capital], correct: 2m, incorrect: -1m, unattempted: 0m);

    private static QuestionMark Mark(string? typed, TypedAnswerVerdict? verdict) =>
        AttemptScorer.Mark(Exam, Capital, Array.Empty<Guid>(), typed, verdict);

    [Fact]
    public void AnAnswerJudgedRight_IsRight_EvenWhenItIsNotAnAcceptedAnswer() =>
        Assert.Equal(new QuestionMark(AnswerVerdict.Correct, 2m), Mark("The French capital is Paris.", TypedAnswerVerdict.Correct));

    [Fact]
    public void AnAnswerJudgedWrong_IsWrong() =>
        Assert.Equal(new QuestionMark(AnswerVerdict.Wrong, -1m), Mark("Lyon", TypedAnswerVerdict.Wrong));

    [Fact]
    public void AnAnswerWithNoVerdict_IsJudgedByTheAcceptedAnswersAlone() =>
        Assert.Equal(new QuestionMark(AnswerVerdict.Wrong, -1m), Mark("The French capital is Paris.", null));

    [Fact]
    public void AnAcceptedAnswer_IsRight_WhateverTheEvaluatorSaid() =>
        // A key corrected to include the candidate's words fixes the mark without asking the evaluator again.
        Assert.Equal(new QuestionMark(AnswerVerdict.Correct, 2m), Mark("paris", TypedAnswerVerdict.Wrong));

    [Fact]
    public void ABlankAnswer_IsUnanswered_WhateverTheVerdict() =>
        Assert.Equal(new QuestionMark(AnswerVerdict.Unanswered, 0m), Mark("   ", TypedAnswerVerdict.Correct));

    [Fact]
    public void AVerdict_ChangesNothingForAQuestionAnsweredByChoosingOptions()
    {
        var single = Fixtures.Question();
        var exam = Fixtures.Exam([single], correct: 2m, incorrect: -1m);

        var mark = AttemptScorer.Mark(exam, single, [single.Wrong()], answerText: null, TypedAnswerVerdict.Correct);

        Assert.Equal(new QuestionMark(AnswerVerdict.Wrong, -1m), mark);
    }

    [Fact]
    public void TheScore_UsesTheVerdictsOfTheAnswers()
    {
        var single = Fixtures.Question();
        var exam = Fixtures.Exam([Capital, single], correct: 2m, incorrect: -1m);
        var attempt = Attempt.Start(exam.Id, Guid.NewGuid(), 1, Fixtures.Now, Fixtures.Now.AddHours(1));
        attempt.RecordTextAnswer(Capital.Id, "The French capital is Paris.", Fixtures.Now);
        attempt.RecordAnswer(single.Id, single.Correct(), Fixtures.Now);
        attempt.SubmitForGrading(Fixtures.Now.AddMinutes(5), [Capital.Id]);
        attempt.RecordEvaluation(Capital.Id, TypedAnswerVerdict.Correct, "Same capital.", "gemini-test", Fixtures.Now.AddMinutes(6));

        var score = AttemptScorer.Score(
            exam, new Dictionary<Guid, QuestionSnapshot> { [Capital.Id] = Capital, [single.Id] = single }, attempt.Answers.ToList());

        Assert.Equal(new AttemptScore(4m, 4m), score);
    }
}

/// <summary>An attempt whose typed answers are checked by the AI evaluator after it ends: it waits in Grading, with no score, until they are.</summary>
public class AttemptGradingTests
{
    private static readonly DateTime Later = Fixtures.Now.AddMinutes(30);

    private readonly QuestionSnapshot _capital = Fixtures.TextQuestion("Paris");
    private readonly QuestionSnapshot _river = Fixtures.TextQuestion("Seine");
    private readonly QuestionSnapshot _single = Fixtures.Question();

    private Attempt OpenWithTypedAnswers()
    {
        var attempt = Attempt.Start(Guid.NewGuid(), Guid.NewGuid(), 1, Fixtures.Now, Fixtures.Now.AddHours(1));
        attempt.RecordTextAnswer(_capital.Id, "The French capital is Paris.", Fixtures.Now);
        attempt.RecordTextAnswer(_river.Id, "The Loire", Fixtures.Now);
        attempt.RecordAnswer(_single.Id, _single.Correct(), Fixtures.Now);
        return attempt;
    }

    [Fact]
    public void SubmittingForGrading_EndsTheAttempt_HoldsBackTheScore_AndQueuesTheNamedAnswers()
    {
        var attempt = OpenWithTypedAnswers();

        attempt.SubmitForGrading(Later, [_capital.Id, _river.Id]);

        Assert.Equal(AttemptStatus.Grading, attempt.Status);
        Assert.Null(attempt.Score);
        Assert.Null(attempt.MaxScore);
        Assert.Equal(Later, attempt.SubmittedAtUtc);
        Assert.True(attempt.IsAwaitingEvaluation);
        Assert.Equal(new[] { _capital.Id, _river.Id }.Order(), attempt.AnswersAwaitingEvaluation.Select(a => a.QuestionId).Order());
        Assert.All(attempt.AnswersAwaitingEvaluation, a => Assert.Equal(Later, a.EvaluationRequestedAtUtc));
    }

    [Fact]
    public void AnswersNotNamed_AreNotQueued()
    {
        var attempt = OpenWithTypedAnswers();

        attempt.SubmitForGrading(Later, [_capital.Id]);

        Assert.Equal(_capital.Id, Assert.Single(attempt.AnswersAwaitingEvaluation).QuestionId);
    }

    [Fact]
    public void ARunOutAttempt_IsRecordedAsEndedAtItsDeadline_AndAutoSubmitted()
    {
        var attempt = OpenWithTypedAnswers();

        attempt.SubmitForGrading(Fixtures.Now.AddHours(5), [_capital.Id]);

        Assert.True(attempt.AutoSubmitted);
        Assert.Equal(attempt.DeadlineUtc, attempt.SubmittedAtUtc);
        Assert.Equal(AttemptStatus.Grading, attempt.Status);
    }

    [Fact]
    public void AnAttemptEndedByViolations_RecordsThat()
    {
        var attempt = OpenWithTypedAnswers();

        attempt.SubmitForGrading(Later, [_capital.Id], endedByViolations: true);

        Assert.True(attempt.EndedByViolations);
        Assert.True(attempt.AutoSubmitted);
    }

    [Fact]
    public void WithNothingToCheck_ItIsRefused_ForItIsAnOrdinarySubmit()
    {
        var attempt = OpenWithTypedAnswers();

        Assert.Throws<InvalidAttemptError>(() => attempt.SubmitForGrading(Later, []));

        Assert.Equal(AttemptStatus.InProgress, attempt.Status);
    }

    [Fact]
    public void AQuestionTheCandidateDidNotType_CannotBeQueued_AndNothingIsChanged()
    {
        var attempt = OpenWithTypedAnswers();

        Assert.Throws<InvalidAttemptError>(() => attempt.SubmitForGrading(Later, [_capital.Id, _single.Id]));
        Assert.Throws<InvalidAttemptError>(() => attempt.SubmitForGrading(Later, [Guid.NewGuid()]));

        Assert.Equal(AttemptStatus.InProgress, attempt.Status);
    }

    [Fact]
    public void AnAttemptAlreadyOver_CannotBeSubmittedForGrading()
    {
        var attempt = OpenWithTypedAnswers();
        attempt.Submit(Later, 1m, 3m);

        Assert.Throws<AttemptNotInProgressError>(() => attempt.SubmitForGrading(Later, [_capital.Id]));
    }

    [Fact]
    public void APausedAttempt_CannotBeSubmittedForGrading()
    {
        var attempt = OpenWithTypedAnswers();
        attempt.Pause(Fixtures.Now.AddMinutes(1));

        Assert.Throws<AttemptPausedError>(() => attempt.SubmitForGrading(Later, [_capital.Id]));
    }

    [Fact]
    public void AnAttemptBeingGraded_TakesNoMoreAnswers()
    {
        var attempt = OpenWithTypedAnswers();
        attempt.SubmitForGrading(Later, [_capital.Id]);

        Assert.Throws<AttemptNotInProgressError>(() => attempt.RecordTextAnswer(_capital.Id, "Lyon", Later));
        Assert.Throws<AttemptNotInProgressError>(() => attempt.ClearAnswer(_capital.Id, Later));
    }

    [Fact]
    public void AnAttemptBeingGraded_CannotBeInvalidated_YetWithItsOwnReason()
    {
        var attempt = OpenWithTypedAnswers();
        attempt.SubmitForGrading(Later, [_capital.Id]);

        Assert.Throws<AttemptBeingGradedError>(() => attempt.Invalidate(Guid.NewGuid(), "Copied.", Later));
    }

    [Fact]
    public void AnAdministratorEndingTheAttempt_CanAlsoHoldBackTheScore()
    {
        var attempt = OpenWithTypedAnswers();
        var admin = Guid.NewGuid();

        attempt.TerminateForGrading(Later, admin, "Left the room.", [_capital.Id]);

        Assert.Equal(AttemptStatus.Grading, attempt.Status);
        Assert.Null(attempt.Score);
        Assert.Equal(admin, attempt.TerminatedByUserId);
        Assert.Equal("Left the room.", attempt.TerminationReason);
        Assert.True(attempt.IsAwaitingEvaluation);
        Assert.Contains(attempt.DomainEvents, e => e is AttemptTerminatedEvent { Reason: "Left the room." });
    }

    [Fact]
    public void AVerdict_IsKept_WithItsReasonAndModel_AndTakesTheAnswerOutOfTheQueue()
    {
        var attempt = OpenWithTypedAnswers();
        attempt.SubmitForGrading(Later, [_capital.Id, _river.Id]);

        attempt.RecordEvaluation(_capital.Id, TypedAnswerVerdict.Correct, "  It names Paris.  ", "gemini-test", Later.AddSeconds(5));

        var answer = attempt.Answers.Single(a => a.QuestionId == _capital.Id);
        Assert.Equal(TypedAnswerVerdict.Correct, answer.TextVerdict);
        Assert.Equal("It names Paris.", answer.EvaluationReason);
        Assert.Equal("gemini-test", answer.EvaluationModel);
        Assert.Equal(Later.AddSeconds(5), answer.EvaluatedAtUtc);
        Assert.False(answer.IsAwaitingEvaluation);
        Assert.True(attempt.IsAwaitingEvaluation);
    }

    [Fact]
    public void ALongReason_IsCut_AtTheLimit()
    {
        var attempt = OpenWithTypedAnswers();
        attempt.SubmitForGrading(Later, [_capital.Id]);

        attempt.RecordEvaluation(_capital.Id, TypedAnswerVerdict.Wrong, new string('x', 900), "gemini-test", Later);

        Assert.Equal(AttemptAnswer.MaxEvaluationReasonLength, attempt.Answers.Single(a => a.QuestionId == _capital.Id).EvaluationReason!.Length);
    }

    [Fact]
    public void AVerdict_ForAnAnswerThatIsNotWaiting_IsRefused()
    {
        var attempt = OpenWithTypedAnswers();
        attempt.SubmitForGrading(Later, [_capital.Id]);

        Assert.Throws<InvalidAttemptError>(() => attempt.RecordEvaluation(_river.Id, TypedAnswerVerdict.Correct, null, null, Later));
        attempt.RecordEvaluation(_capital.Id, TypedAnswerVerdict.Correct, null, null, Later);
        Assert.Throws<InvalidAttemptError>(() => attempt.RecordEvaluation(_capital.Id, TypedAnswerVerdict.Wrong, null, null, Later));
    }

    [Fact]
    public void AFailure_KeepsTheAnswerInTheQueue_CountsIt_AndSaysWhenToTryAgain()
    {
        var attempt = OpenWithTypedAnswers();
        attempt.SubmitForGrading(Later, [_capital.Id]);

        attempt.RecordEvaluationFailure(_capital.Id, "The service was busy.", Later.AddSeconds(30));
        attempt.RecordEvaluationFailure(_capital.Id, "The service was busy.", Later.AddSeconds(90));

        var answer = Assert.Single(attempt.Answers, a => a.IsAwaitingEvaluation);
        Assert.Equal(2, answer.EvaluationFailures);
        Assert.Equal(Later.AddSeconds(90), answer.EvaluationNextAttemptAtUtc);
        Assert.Equal("The service was busy.", answer.EvaluationLastError);
        Assert.Null(answer.TextVerdict);
        Assert.Null(answer.EvaluationLeaseUntilUtc);
    }

    [Fact]
    public void AVerdictAfterAFailure_ClearsTheFailureRecord()
    {
        var attempt = OpenWithTypedAnswers();
        attempt.SubmitForGrading(Later, [_capital.Id]);
        attempt.RecordEvaluationFailure(_capital.Id, "Timed out.", Later.AddSeconds(30));

        attempt.RecordEvaluation(_capital.Id, TypedAnswerVerdict.Correct, null, "gemini-test", Later.AddMinutes(1));

        var answer = attempt.Answers.Single(a => a.QuestionId == _capital.Id);
        Assert.Equal(0, answer.EvaluationFailures);
        Assert.Null(answer.EvaluationLastError);
        Assert.Null(answer.EvaluationNextAttemptAtUtc);
    }

    [Fact]
    public void TheScore_CannotBeGiven_WhileAnAnswerStillWaitsForItsVerdict()
    {
        var attempt = OpenWithTypedAnswers();
        attempt.SubmitForGrading(Later, [_capital.Id]);

        Assert.Throws<InvalidAttemptError>(() => attempt.ApplyEvaluatedScore(2m, 3m, Later));

        Assert.Equal(AttemptStatus.Grading, attempt.Status);
        Assert.Null(attempt.Score);
    }

    [Fact]
    public void WhenEveryVerdictIsIn_TheScoreIsGiven_AndTheAttemptIsSubmitted()
    {
        var attempt = OpenWithTypedAnswers();
        attempt.SubmitForGrading(Later, [_capital.Id]);
        attempt.RecordEvaluation(_capital.Id, TypedAnswerVerdict.Correct, null, "gemini-test", Later);

        var changed = attempt.ApplyEvaluatedScore(2m, 3m, Later.AddSeconds(1));

        Assert.True(changed);
        Assert.Equal(AttemptStatus.Submitted, attempt.Status);
        Assert.Equal(2m, attempt.Score);
        Assert.Equal(3m, attempt.MaxScore);
        Assert.Empty(attempt.Revisions);
    }

    [Fact]
    public void AnAttemptNotWaitingForVerdicts_CannotBeGivenAScore()
    {
        var attempt = OpenWithTypedAnswers();
        attempt.Submit(Later, 1m, 3m);

        Assert.Throws<AttemptNotGradingError>(() => attempt.ApplyEvaluatedScore(2m, 3m, Later));
    }

    [Fact]
    public void ARestart_OnAGradingAttempt_QueuesThatAnswerAgain_AndNeedsNoRevision()
    {
        var attempt = OpenWithTypedAnswers();
        attempt.SubmitForGrading(Later, [_capital.Id]);
        attempt.RecordEvaluation(_capital.Id, TypedAnswerVerdict.Wrong, null, "gemini-test", Later);

        attempt.RestartEvaluation(_capital.Id, Later.AddMinutes(1));

        var answer = attempt.Answers.Single(a => a.QuestionId == _capital.Id);
        Assert.True(answer.IsAwaitingEvaluation);
        // The earlier verdict stays in force until the new one replaces it.
        Assert.Equal(TypedAnswerVerdict.Wrong, answer.TextVerdict);
        Assert.Null(attempt.PendingRevisionReason);
    }

    [Fact]
    public void ARestart_OnAnAttemptThatIsNotGrading_IsRefused()
    {
        var attempt = OpenWithTypedAnswers();

        Assert.Throws<AttemptNotGradingError>(() => attempt.RestartEvaluation(_capital.Id, Later));
    }
}

/// <summary>A submitted attempt whose typed answers are checked again after staff corrected a question's key keeps its result until the new verdicts are in.</summary>
public class AttemptReevaluationTests
{
    private static readonly DateTime Later = Fixtures.Now.AddMinutes(30);
    private readonly QuestionSnapshot _capital = Fixtures.TextQuestion("Paris");

    private Attempt Graded(TypedAnswerVerdict verdict, decimal score)
    {
        var attempt = Attempt.Start(Guid.NewGuid(), Guid.NewGuid(), 1, Fixtures.Now, Fixtures.Now.AddHours(1));
        attempt.RecordTextAnswer(_capital.Id, "The French capital is Paris.", Fixtures.Now);
        attempt.SubmitForGrading(Later, [_capital.Id]);
        attempt.RecordEvaluation(_capital.Id, verdict, "First reason.", "gemini-test", Later);
        attempt.ApplyEvaluatedScore(score, 1m, Later);
        return attempt;
    }

    [Fact]
    public void ARequest_KeepsTheScoreAndTheVerdict_AndRemembersWhy()
    {
        var attempt = Graded(TypedAnswerVerdict.Wrong, 0m);

        attempt.RequestReevaluation([_capital.Id], "The answer key was corrected.", Later.AddHours(1));

        Assert.Equal(AttemptStatus.Submitted, attempt.Status);
        Assert.Equal(0m, attempt.Score);
        var answer = attempt.Answers.Single();
        Assert.True(answer.IsAwaitingEvaluation);
        Assert.Equal(TypedAnswerVerdict.Wrong, answer.TextVerdict);
        Assert.Equal("The answer key was corrected.", attempt.PendingRevisionReason);
    }

    [Fact]
    public void WhenTheNewVerdictChangesTheScore_ItIsRevised_WithTheReason()
    {
        var attempt = Graded(TypedAnswerVerdict.Wrong, 0m);
        attempt.RequestReevaluation([_capital.Id], "The answer key was corrected.", Later.AddHours(1));
        attempt.RecordEvaluation(_capital.Id, TypedAnswerVerdict.Correct, "Second reason.", "gemini-test", Later.AddHours(1));

        var changed = attempt.ApplyEvaluatedScore(1m, 1m, Later.AddHours(1));

        Assert.True(changed);
        Assert.Equal(1m, attempt.Score);
        var revision = Assert.Single(attempt.Revisions);
        Assert.Equal("The answer key was corrected.", revision.Reason);
        Assert.Equal(0m, revision.PreviousScore);
        Assert.Equal(1m, revision.NewScore);
        Assert.Null(attempt.PendingRevisionReason);
    }

    [Fact]
    public void WhenTheNewVerdictChangesNothing_NoRevisionIsRecorded_AndTheReasonIsForgotten()
    {
        var attempt = Graded(TypedAnswerVerdict.Correct, 1m);
        attempt.RequestReevaluation([_capital.Id], "The answer key was corrected.", Later.AddHours(1));
        attempt.RecordEvaluation(_capital.Id, TypedAnswerVerdict.Correct, "Still right.", "gemini-test", Later.AddHours(1));

        var changed = attempt.ApplyEvaluatedScore(1m, 1m, Later.AddHours(1));

        Assert.False(changed);
        Assert.Empty(attempt.Revisions);
        Assert.Null(attempt.PendingRevisionReason);
    }

    [Fact]
    public void ARequest_OnAnAttemptThatHasNoScoreYet_IsRefused()
    {
        var attempt = Attempt.Start(Guid.NewGuid(), Guid.NewGuid(), 1, Fixtures.Now, Fixtures.Now.AddHours(1));
        attempt.RecordTextAnswer(_capital.Id, "Paris", Fixtures.Now);

        Assert.Throws<AttemptNotSubmittedError>(() => attempt.RequestReevaluation([_capital.Id], "Corrected.", Later));
    }

    [Fact]
    public void ALongReason_IsCut_ToWhatARevisionKeeps()
    {
        var attempt = Graded(TypedAnswerVerdict.Wrong, 0m);

        attempt.RequestReevaluation([_capital.Id], new string('y', 800), Later);

        Assert.Equal(Attempt.MaxRevisionReasonLength, attempt.PendingRevisionReason!.Length);
    }
}
