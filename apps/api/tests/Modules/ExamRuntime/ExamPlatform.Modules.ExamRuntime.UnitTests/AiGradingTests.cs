using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application;
using ExamPlatform.Modules.ExamRuntime.Application.Commands;
using ExamPlatform.Modules.ExamRuntime.Application.Grading;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.ExamRuntime.Infrastructure.AiEvaluation;
using ExamPlatform.Modules.Notifications.Contracts;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.SharedKernel.Application;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>
/// Which of an attempt's typed answers the evaluator is asked about: only one the candidate typed that matches no accepted answer word for
/// word. An exact match is right without it, a blank one is unanswered, and a question answered by choosing options is not its to judge.
/// </summary>
public class TypedAnswerChecksTests
{
    private readonly QuestionSnapshot _capital = Fixtures.TextQuestion("Paris");
    private readonly QuestionSnapshot _river = Fixtures.TextQuestion("Seine");
    private readonly QuestionSnapshot _single = Fixtures.Question();

    private Dictionary<Guid, QuestionSnapshot> Questions => new() { [_capital.Id] = _capital, [_river.Id] = _river, [_single.Id] = _single };

    private static Attempt Open() => Attempt.Start(Guid.NewGuid(), Guid.NewGuid(), 1, Fixtures.Now, Fixtures.Now.AddHours(1));

    [Fact]
    public void ATypedAnswerThatMatchesNoAcceptedAnswer_IsChecked()
    {
        var attempt = Open();
        attempt.RecordTextAnswer(_capital.Id, "The French capital is Paris.", Fixtures.Now);

        Assert.Equal([_capital.Id], TypedAnswerChecks.QuestionsToCheck(attempt, Questions));
    }

    [Fact]
    public void AnExactMatch_IsNotChecked_WhateverItsCaseAndSpacing()
    {
        var attempt = Open();
        attempt.RecordTextAnswer(_capital.Id, "  PARIS ", Fixtures.Now);

        Assert.Empty(TypedAnswerChecks.QuestionsToCheck(attempt, Questions));
    }

    [Fact]
    public void AnAnswerChosenFromOptions_IsNeverChecked()
    {
        var attempt = Open();
        attempt.RecordAnswer(_single.Id, _single.Wrong(), Fixtures.Now);

        Assert.Empty(TypedAnswerChecks.QuestionsToCheck(attempt, Questions));
    }

    [Fact]
    public void AnAnswerToAQuestionTheExamDoesNotHave_IsNotChecked()
    {
        var attempt = Open();
        attempt.RecordTextAnswer(Guid.NewGuid(), "Anything", Fixtures.Now);

        Assert.Empty(TypedAnswerChecks.QuestionsToCheck(attempt, Questions));
    }

    [Fact]
    public void EveryAnswerThatNeedsIt_IsNamed_AndNoOtherOne()
    {
        var attempt = Open();
        attempt.RecordTextAnswer(_capital.Id, "paris", Fixtures.Now);
        attempt.RecordTextAnswer(_river.Id, "The Loire", Fixtures.Now);
        attempt.RecordAnswer(_single.Id, _single.Correct(), Fixtures.Now);

        Assert.Equal([_river.Id], TypedAnswerChecks.QuestionsToCheck(attempt, Questions));
    }
}

/// <summary>
/// Ending an attempt when the AI evaluator is set up: an attempt holding a typed answer the accepted answers do not settle is closed without a
/// score and waits in grading, whichever way it ends (the candidate submits, time runs out, the violation limit is reached, an administrator
/// ends it). With no evaluator, or nothing for it to judge, it is scored at once, exactly as it always was.
/// </summary>
public class AttemptClosingWithTheEvaluatorTests
{
    private readonly Guid _candidate = Guid.NewGuid();
    private readonly Guid _admin = Guid.NewGuid();
    private readonly FakeClock _clock = new(Fixtures.Now);
    private readonly IExamCatalog _catalog = Substitute.For<IExamCatalog>();
    private readonly IQuestionBank _bank = Substitute.For<IQuestionBank>();
    private readonly IAttemptRepository _attempts = Substitute.For<IAttemptRepository>();
    private readonly IExamRuntimeUnitOfWork _unitOfWork = Substitute.For<IExamRuntimeUnitOfWork>();
    private readonly ITextAnswerEvaluator _evaluator = Substitute.For<ITextAnswerEvaluator>();
    private readonly QuestionSnapshot _capital = Fixtures.TextQuestion("Paris");
    private readonly QuestionSnapshot _single = Fixtures.Question();
    private readonly ExamSnapshot _exam;

    public AttemptClosingWithTheEvaluatorTests()
    {
        _exam = Fixtures.Exam([_capital, _single], correct: 2m, incorrect: -1m);
        _catalog.FindAsync(_exam.Id, Arg.Any<CancellationToken>()).Returns(_exam);
        _bank.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<QuestionSnapshot>>(
                new[] { _capital, _single }.Where(q => call.Arg<IReadOnlyCollection<Guid>>().Contains(q.Id)).ToList()));
        _evaluator.IsEnabled.Returns(true);
    }

    private AttemptCloser Closer => new(_bank, _unitOfWork, _clock, _evaluator);
    private AttemptAccess Access => new(_attempts, _catalog, Closer, _clock, new ProctorPolicy());
    private SubmitAttemptHandler Submit => new(Access, Closer, new AttemptViewBuilder(_bank, _clock), _unitOfWork);

    private Attempt Open(string? typed, DateTime? deadline = null)
    {
        var attempt = Attempt.Start(_exam.Id, _candidate, 1, Fixtures.Now.AddMinutes(-5), deadline ?? Fixtures.Now.AddMinutes(25));
        if (typed is not null)
            attempt.RecordTextAnswer(_capital.Id, typed, Fixtures.Now.AddMinutes(-4));
        attempt.RecordAnswer(_single.Id, _single.Correct(), Fixtures.Now.AddMinutes(-3));
        _attempts.GetByIdAsync(attempt.Id, Arg.Any<CancellationToken>()).Returns(attempt);
        return attempt;
    }

    [Fact]
    public async Task ATypedAnswerThatMatchesNoAcceptedAnswer_SendsTheSubmittedAttemptToGrading_WithNoScore()
    {
        var attempt = Open("The French capital is Paris.");

        var shown = await Submit.HandleAsync(attempt.Id, _candidate, CancellationToken.None);

        Assert.Equal(AttemptStatus.Grading, attempt.Status);
        Assert.Equal(AttemptStatus.Grading, shown.Status);
        Assert.Null(shown.Score);
        Assert.Null(shown.MaxScore);
        Assert.Equal(_capital.Id, Assert.Single(attempt.AnswersAwaitingEvaluation).QuestionId);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnAttemptWhoseTypedAnswersAllMatch_IsScoredAtOnce_WithoutTheEvaluator()
    {
        var attempt = Open("  paris ");

        var shown = await Submit.HandleAsync(attempt.Id, _candidate, CancellationToken.None);

        Assert.Equal(AttemptStatus.Submitted, shown.Status);
        Assert.Equal(4m, shown.Score);
        Assert.False(attempt.IsAwaitingEvaluation);
    }

    [Fact]
    public async Task WhenNoEvaluatorIsSetUp_ADifferentlyWordedAnswer_IsMarkedByTheAcceptedAnswersAlone_AndNothingWaits()
    {
        _evaluator.IsEnabled.Returns(false);
        var attempt = Open("The French capital is Paris.");

        var shown = await Submit.HandleAsync(attempt.Id, _candidate, CancellationToken.None);

        Assert.Equal(AttemptStatus.Submitted, shown.Status);
        // Wrong for the typed one (-1) and right for the chosen one (+2).
        Assert.Equal(1m, shown.Score);
        Assert.False(attempt.IsAwaitingEvaluation);
    }

    [Fact]
    public async Task WithNoEvaluatorRegisteredAtAll_TheAttemptIsScoredAtOnce()
    {
        var attempt = Open("The French capital is Paris.");

        await new AttemptCloser(_bank, _unitOfWork, _clock).CloseAsync(attempt, _exam, CancellationToken.None);

        Assert.Equal(AttemptStatus.Submitted, attempt.Status);
        Assert.Equal(1m, attempt.Score);
    }

    [Fact]
    public async Task AnAttemptWithNoTypedAnswer_IsNeverSentToGrading()
    {
        var attempt = Open(typed: null);

        var shown = await Submit.HandleAsync(attempt.Id, _candidate, CancellationToken.None);

        Assert.Equal(AttemptStatus.Submitted, shown.Status);
        Assert.Equal(2m, shown.Score);
    }

    [Fact]
    public async Task AnAttemptThatRanOutOfTime_IsSentToGrading_AndRecordedAsEndedAtItsDeadline()
    {
        var deadline = Fixtures.Now.AddMinutes(10);
        var attempt = Open("The French capital is Paris.", deadline);
        _clock.UtcNow = Fixtures.Now.AddHours(3);

        // Whoever next looks at the attempt closes it: here, the candidate loading it.
        await Access.LoadOwnedAsync(attempt.Id, _candidate, CancellationToken.None);

        Assert.Equal(AttemptStatus.Grading, attempt.Status);
        Assert.True(attempt.AutoSubmitted);
        Assert.Equal(deadline, attempt.SubmittedAtUtc);
        Assert.Null(attempt.Score);
    }

    [Fact]
    public async Task AnAttemptEndedForLeavingThePageTooOften_IsSentToGrading_AndSaysWhy()
    {
        var attempt = Open("The French capital is Paris.");

        await Closer.CloseForViolationsAsync(attempt, _exam, CancellationToken.None);

        Assert.Equal(AttemptStatus.Grading, attempt.Status);
        Assert.True(attempt.EndedByViolations);
        Assert.True(attempt.AutoSubmitted);
    }

    [Fact]
    public async Task AnAttemptAnAdministratorEnds_IsSentToGrading_WithTheReasonKept()
    {
        var attempt = Open("The French capital is Paris.");

        await Closer.TerminateAsync(attempt, _exam, _admin, "Caught with a phone.", CancellationToken.None);

        Assert.Equal(AttemptStatus.Grading, attempt.Status);
        Assert.True(attempt.IsTerminated);
        Assert.Equal("Caught with a phone.", attempt.TerminationReason);
        Assert.Equal(_admin, attempt.TerminatedByUserId);
        Assert.Null(attempt.Score);
    }

    [Fact]
    public async Task AnAttemptAnAdministratorEnds_IsScoredAtOnce_WhenNothingNeedsTheEvaluator()
    {
        var attempt = Open("paris");

        await Closer.TerminateAsync(attempt, _exam, _admin, "Ended early.", CancellationToken.None);

        Assert.Equal(AttemptStatus.Submitted, attempt.Status);
        Assert.Equal(4m, attempt.Score);
    }

    [Fact]
    public async Task AnExamWhoseQuestionsCannotBeRead_StillFailsAtSubmit_AsItDidBefore()
    {
        var attempt = Open("The French capital is Paris.");
        _bank.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<QuestionSnapshot>>([]));

        await Assert.ThrowsAsync<ExamContentUnavailableError>(() => Submit.HandleAsync(attempt.Id, _candidate, CancellationToken.None));

        Assert.Equal(AttemptStatus.InProgress, attempt.Status);
    }
}

/// <summary>The database side of judging typed answers: what is asked, how a verdict is saved and scored, and how a failure waits.</summary>
public class TypedAnswerGradingTests
{
    private static readonly DateTime SubmittedAt = Fixtures.Now.AddMinutes(30);

    private readonly FakeClock _clock = new(Fixtures.Now.AddMinutes(31));
    private readonly IExamCatalog _catalog = Substitute.For<IExamCatalog>();
    private readonly IQuestionBank _bank = Substitute.For<IQuestionBank>();
    private readonly IAttemptRepository _attempts = Substitute.For<IAttemptRepository>();
    private readonly IExamRuntimeUnitOfWork _unitOfWork = Substitute.For<IExamRuntimeUnitOfWork>();
    private readonly IAttemptLock _hold = Substitute.For<IAttemptLock>();
    private readonly AiGradingOptions _settings = new();
    private readonly QuestionSnapshot _capital;
    private readonly QuestionSnapshot _river = Fixtures.TextQuestion("Seine");
    private readonly QuestionSnapshot _single = Fixtures.Question();
    private readonly ExamSnapshot _exam;

    public TypedAnswerGradingTests()
    {
        _capital = Fixtures.TextQuestion("Paris") with { Text = "<p>Capital of <b>France</b>?</p>" };
        _exam = Fixtures.Exam([_capital, _river, _single], correct: 2m, incorrect: -1m);
        _catalog.FindAsync(_exam.Id, Arg.Any<CancellationToken>()).Returns(_exam);
        _bank.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<QuestionSnapshot>>(
                new[] { _capital, _river, _single }.Where(q => call.Arg<IReadOnlyCollection<Guid>>().Contains(q.Id)).ToList()));
        _unitOfWork.LockAttemptAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(_hold);
    }

    private TypedAnswerGrading Grading => new(_attempts, _catalog, _bank, _unitOfWork, new ProctorPolicy(), _clock, Options.Create(_settings));

    // An attempt that ended with a typed answer to each of the named questions that no accepted answer settles, waiting for the evaluator.
    private Attempt Graded(params QuestionSnapshot[] typed)
    {
        var attempt = Attempt.Start(_exam.Id, Guid.NewGuid(), 1, Fixtures.Now, Fixtures.Now.AddHours(1));
        foreach (var question in typed)
            attempt.RecordTextAnswer(question.Id, "Something worded differently", Fixtures.Now.AddMinutes(1));
        attempt.RecordAnswer(_single.Id, _single.Correct(), Fixtures.Now.AddMinutes(2));
        attempt.SubmitForGrading(SubmittedAt, typed.Select(q => q.Id).ToList());
        _attempts.GetByIdAsync(attempt.Id, Arg.Any<CancellationToken>()).Returns(attempt);
        return attempt;
    }

    private static GradingClaim ClaimOf(Attempt attempt, QuestionSnapshot question) =>
        new(attempt.Id, question.Id, attempt.Answers.Single(a => a.QuestionId == question.Id).EvaluationRequestedAtUtc!.Value);

    private static TextEvaluation Verdict(TypedAnswerVerdict verdict) => new(verdict, "Because.", "gemini-test");

    // ---- taking answers off the queue ---------------------------------------------------------------------------

    [Fact]
    public async Task ClaimingAnswers_HoldsThemForTheConfiguredLease()
    {
        var claim = new GradingClaim(Guid.NewGuid(), Guid.NewGuid(), Fixtures.Now);
        _settings.LeaseMinutes = 7;
        _attempts.ClaimAnswersAwaitingEvaluationAsync(_clock.UtcNow, _clock.UtcNow.AddMinutes(7), 1, Arg.Any<CancellationToken>()).Returns([claim]);

        var claimed = await Grading.ClaimAsync(1, CancellationToken.None);

        Assert.Equal([claim], claimed);
    }

    // ---- what is asked ------------------------------------------------------------------------------------------

    [Fact]
    public async Task WhatIsAsked_IsTheQuestionInPlainWords_TheAcceptedAnswers_AndWhatWasTyped()
    {
        var attempt = Graded(_capital);

        var request = await Grading.PrepareAsync(ClaimOf(attempt, _capital), CancellationToken.None);

        Assert.NotNull(request);
        Assert.Equal("Capital of France?", request.QuestionText);
        Assert.Equal(["Paris"], request.AcceptedAnswers);
        Assert.Equal("Something worded differently", request.CandidateAnswer);
    }

    [Fact]
    public async Task NothingIsAsked_ForAnAnswerThatIsNotWaitingAnyMore()
    {
        var attempt = Graded(_capital);
        var claim = ClaimOf(attempt, _capital);
        attempt.RecordEvaluation(_capital.Id, TypedAnswerVerdict.Correct, null, "gemini-test", _clock.UtcNow);

        Assert.Null(await Grading.PrepareAsync(claim, CancellationToken.None));
    }

    [Fact]
    public async Task NothingIsAsked_ForAClaimOfAnEarlierQueueingOfTheAnswer()
    {
        var attempt = Graded(_capital);
        var stale = ClaimOf(attempt, _capital) with { RequestedAtUtc = SubmittedAt.AddMinutes(-10) };

        Assert.Null(await Grading.PrepareAsync(stale, CancellationToken.None));
    }

    [Fact]
    public async Task AQuestionThatCannotBeRead_CannotBeAskedAbout()
    {
        var attempt = Graded(_capital);
        _bank.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<QuestionSnapshot>>([]));

        await Assert.ThrowsAsync<ExamContentUnavailableError>(() => Grading.PrepareAsync(ClaimOf(attempt, _capital), CancellationToken.None));
    }

    // ---- saving a verdict ---------------------------------------------------------------------------------------

    [Fact]
    public async Task TheLastVerdict_ScoresTheAttempt_InsideTheLock()
    {
        var attempt = Graded(_capital);

        await Grading.RecordAsync(ClaimOf(attempt, _capital), Verdict(TypedAnswerVerdict.Correct), CancellationToken.None);

        Assert.Equal(AttemptStatus.Submitted, attempt.Status);
        // Right for the typed answer (+2) and for the chosen one (+2), of 6 available.
        Assert.Equal(4m, attempt.Score);
        Assert.Equal(6m, attempt.MaxScore);
        var answer = attempt.Answers.Single(a => a.QuestionId == _capital.Id);
        Assert.Equal(TypedAnswerVerdict.Correct, answer.TextVerdict);
        Assert.Equal("Because.", answer.EvaluationReason);
        Assert.Equal("gemini-test", answer.EvaluationModel);
        Assert.False(answer.IsAwaitingEvaluation);
        await _unitOfWork.Received(1).LockAttemptAsync(attempt.Id, true, Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _hold.Received(1).CompleteAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AWrongVerdict_ScoresTheAnswerWrong()
    {
        var attempt = Graded(_capital);

        await Grading.RecordAsync(ClaimOf(attempt, _capital), Verdict(TypedAnswerVerdict.Wrong), CancellationToken.None);

        Assert.Equal(AttemptStatus.Submitted, attempt.Status);
        Assert.Equal(1m, attempt.Score);
    }

    [Fact]
    public async Task AVerdictWhileAnotherAnswerIsStillWaiting_DoesNotScoreTheAttemptYet()
    {
        var attempt = Graded(_capital, _river);

        await Grading.RecordAsync(ClaimOf(attempt, _capital), Verdict(TypedAnswerVerdict.Correct), CancellationToken.None);

        Assert.Equal(AttemptStatus.Grading, attempt.Status);
        Assert.Null(attempt.Score);
        Assert.Equal(_river.Id, Assert.Single(attempt.AnswersAwaitingEvaluation).QuestionId);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheVerdictThatCompletesTheSet_ScoresWithEveryVerdict()
    {
        var attempt = Graded(_capital, _river);

        await Grading.RecordAsync(ClaimOf(attempt, _capital), Verdict(TypedAnswerVerdict.Correct), CancellationToken.None);
        await Grading.RecordAsync(ClaimOf(attempt, _river), Verdict(TypedAnswerVerdict.Wrong), CancellationToken.None);

        Assert.Equal(AttemptStatus.Submitted, attempt.Status);
        // +2 (capital) -1 (river) +2 (chosen), of 6.
        Assert.Equal(3m, attempt.Score);
        Assert.Equal(6m, attempt.MaxScore);
    }

    [Fact]
    public async Task AVerdictAskedForBeforeTheAnswerWasQueuedAgain_IsNotKept()
    {
        var attempt = Graded(_capital);
        var stale = ClaimOf(attempt, _capital) with { RequestedAtUtc = SubmittedAt.AddMinutes(-10) };

        await Grading.RecordAsync(stale, Verdict(TypedAnswerVerdict.Correct), CancellationToken.None);

        Assert.Equal(AttemptStatus.Grading, attempt.Status);
        Assert.True(attempt.IsAwaitingEvaluation);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ASubmittedAttemptQueuedAgainAfterAKeyCorrection_HasItsScoreRevisedWithTheReason()
    {
        var attempt = Attempt.Start(_exam.Id, Guid.NewGuid(), 1, Fixtures.Now, Fixtures.Now.AddHours(1));
        attempt.RecordTextAnswer(_capital.Id, "Something worded differently", Fixtures.Now.AddMinutes(1));
        attempt.Submit(SubmittedAt, score: -1m, maxScore: 2m);
        attempt.RequestReevaluation([_capital.Id], "The key now accepts more answers.", _clock.UtcNow);
        _attempts.GetByIdAsync(attempt.Id, Arg.Any<CancellationToken>()).Returns(attempt);
        var exam = Fixtures.Exam([_capital], correct: 2m, incorrect: -1m);
        _catalog.FindAsync(attempt.ExamId, Arg.Any<CancellationToken>()).Returns(exam);
        _bank.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<QuestionSnapshot>>([_capital]));
        var claim = new GradingClaim(attempt.Id, _capital.Id, attempt.Answers.Single().EvaluationRequestedAtUtc!.Value);

        await Grading.RecordAsync(claim, Verdict(TypedAnswerVerdict.Correct), CancellationToken.None);

        Assert.Equal(AttemptStatus.Submitted, attempt.Status);
        Assert.Equal(2m, attempt.Score);
        var revision = Assert.Single(attempt.Revisions);
        Assert.Equal(-1m, revision.PreviousScore);
        Assert.Equal(2m, revision.NewScore);
        Assert.Equal("The key now accepts more answers.", revision.Reason);
        Assert.Null(attempt.PendingRevisionReason);
    }

    [Fact]
    public async Task AnAttemptThatCannotBeScored_KeepsItsVerdictsOutOfTheDatabase_SoTheAnswerIsAskedAgain()
    {
        var attempt = Graded(_capital);
        _catalog.FindAsync(_exam.Id, Arg.Any<CancellationToken>()).Returns((ExamSnapshot?)null);

        await Assert.ThrowsAsync<ExamContentUnavailableError>(() =>
            Grading.RecordAsync(ClaimOf(attempt, _capital), Verdict(TypedAnswerVerdict.Correct), CancellationToken.None));

        // Nothing was saved and the lock was never completed, so disposing it rolls the verdict back with it.
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _hold.DidNotReceive().CompleteAsync(Arg.Any<CancellationToken>());
    }

    // ---- a worker that stops --------------------------------------------------------------------------------------

    private static void Hold(AttemptAnswer answer, DateTime until) =>
        typeof(AttemptAnswer).GetProperty(nameof(AttemptAnswer.EvaluationLeaseUntilUtc))!.SetValue(answer, until);

    [Fact]
    public async Task AnAnswerAStoppingWorkerLetsGo_IsHeldNoMore_AndNoFailureIsCounted()
    {
        var attempt = Graded(_capital);
        var answer = attempt.Answers.Single(a => a.QuestionId == _capital.Id);
        Hold(answer, _clock.UtcNow.AddMinutes(5));

        await Grading.ReleaseAsync(ClaimOf(attempt, _capital), CancellationToken.None);

        Assert.Null(answer.EvaluationLeaseUntilUtc);
        Assert.Equal(0, answer.EvaluationFailures);
        Assert.Null(answer.EvaluationNextAttemptAtUtc);
        Assert.True(answer.IsAwaitingEvaluation);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnAnswerQueuedAgainSinceTheClaim_IsNotLetGoOfByTheOldClaim()
    {
        var attempt = Graded(_capital);
        var answer = attempt.Answers.Single(a => a.QuestionId == _capital.Id);
        Hold(answer, _clock.UtcNow.AddMinutes(5));
        var stale = ClaimOf(attempt, _capital) with { RequestedAtUtc = SubmittedAt.AddMinutes(-10) };

        await Grading.ReleaseAsync(stale, CancellationToken.None);

        // Whoever holds the new queueing keeps it.
        Assert.NotNull(answer.EvaluationLeaseUntilUtc);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ---- a failure ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task AFailure_LeavesTheAnswerWaiting_AndTheAttemptInGrading_UntilTheWaitIsOver()
    {
        var attempt = Graded(_capital);

        await Grading.FailAsync(ClaimOf(attempt, _capital), "The AI service answered 503.", permanent: false, CancellationToken.None);

        var answer = attempt.Answers.Single(a => a.QuestionId == _capital.Id);
        Assert.True(answer.IsAwaitingEvaluation);
        Assert.Equal(1, answer.EvaluationFailures);
        Assert.Equal("The AI service answered 503.", answer.EvaluationLastError);
        Assert.Equal(_clock.UtcNow.AddSeconds(30), answer.EvaluationNextAttemptAtUtc);
        Assert.Null(answer.EvaluationLeaseUntilUtc);
        Assert.Equal(AttemptStatus.Grading, attempt.Status);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheWaitDoublesWithEachFailure_AndNeverPassesTheLongest()
    {
        var attempt = Graded(_capital);
        var claim = ClaimOf(attempt, _capital);
        var answer = attempt.Answers.Single(a => a.QuestionId == _capital.Id);
        var waits = new List<TimeSpan>();

        for (var i = 0; i < 12; i++)
        {
            await Grading.FailAsync(claim, "Busy.", permanent: false, CancellationToken.None);
            waits.Add(answer.EvaluationNextAttemptAtUtc!.Value - _clock.UtcNow);
        }

        Assert.Equal([TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(4)], waits.Take(4));
        Assert.Equal(TimeSpan.FromMinutes(10), waits[^1]);
        Assert.All(waits, wait => Assert.True(wait <= TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public async Task AFailureWaitingCannotCure_WaitsTheLongestAtOnce()
    {
        var attempt = Graded(_capital);

        await Grading.FailAsync(ClaimOf(attempt, _capital), "The AI service answered 403 (PERMISSION_DENIED).", permanent: true, CancellationToken.None);

        var answer = attempt.Answers.Single(a => a.QuestionId == _capital.Id);
        Assert.Equal(_clock.UtcNow.AddMinutes(10), answer.EvaluationNextAttemptAtUtc);
        Assert.True(answer.IsAwaitingEvaluation);
    }

    [Fact]
    public async Task AFailureOfAnEarlierQueueingOfTheAnswer_IsNotRecordedAgainstTheNewOne()
    {
        var attempt = Graded(_capital);
        var stale = ClaimOf(attempt, _capital) with { RequestedAtUtc = SubmittedAt.AddMinutes(-10) };

        await Grading.FailAsync(stale, "Busy.", permanent: false, CancellationToken.None);

        Assert.Equal(0, attempt.Answers.Single(a => a.QuestionId == _capital.Id).EvaluationFailures);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}

/// <summary>
/// A question's answer key being corrected while the evaluator is set up (FR-31): a typed answer that was, or is yet to be, judged is judged
/// again against the new key, and the attempt's score is revised when the new verdict is in, not before.
/// </summary>
public class AttemptRescorerWithTheEvaluatorTests
{
    private readonly FakeClock _clock = new(Fixtures.Now);
    private readonly IAttemptRepository _attempts = Substitute.For<IAttemptRepository>();
    private readonly IExamCatalog _catalog = Substitute.For<IExamCatalog>();
    private readonly IQuestionBank _bank = Substitute.For<IQuestionBank>();
    private readonly IExamRuntimeUnitOfWork _unitOfWork = Substitute.For<IExamRuntimeUnitOfWork>();
    private readonly IDisputeRepository _disputes = Substitute.For<IDisputeRepository>();
    private readonly IRequestContext _requestContext = Substitute.For<IRequestContext>();
    private readonly IInAppNotifier _notifier = Substitute.For<IInAppNotifier>();
    private readonly ITextAnswerEvaluator _evaluator = Substitute.For<ITextAnswerEvaluator>();
    private readonly QuestionSnapshot _capital = Fixtures.TextQuestion("Paris");
    private readonly ExamSnapshot _exam;

    public AttemptRescorerWithTheEvaluatorTests()
    {
        _exam = Fixtures.Exam([_capital], correct: 2m, incorrect: -1m);
        _evaluator.IsEnabled.Returns(true);
        _disputes.ListOpenForQuestionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([]);
        _requestContext.UserId.Returns(Guid.NewGuid());
        _catalog.FindAsync(_exam.Id, Arg.Any<CancellationToken>()).Returns(_exam);
        // The key as corrected: "Lyon" is the answer now, and "Paris" no longer is.
        SetKey("Lyon");
    }

    private AttemptRescorer Rescorer(ITextAnswerEvaluator? evaluator) =>
        new(_attempts, _catalog, _bank, _disputes, _unitOfWork, _requestContext, _clock, _notifier, new ProctorPolicy(), evaluator);

    private void SetKey(params string[] accepted) =>
        _bank.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<QuestionSnapshot>>([_capital with { AcceptedAnswers = accepted }]));

    private Attempt SubmittedWithAJudgedAnswer(string typed, TypedAnswerVerdict verdict)
    {
        var attempt = Attempt.Start(_exam.Id, Guid.NewGuid(), 1, Fixtures.Now.AddHours(-1), Fixtures.Now.AddHours(1));
        attempt.RecordTextAnswer(_capital.Id, typed, Fixtures.Now.AddMinutes(-50));
        attempt.SubmitForGrading(Fixtures.Now.AddMinutes(-40), [_capital.Id]);
        attempt.RecordEvaluation(_capital.Id, verdict, "Judged against the old key.", "gemini-test", Fixtures.Now.AddMinutes(-39));
        attempt.ApplyEvaluatedScore(verdict == TypedAnswerVerdict.Correct ? 2m : -1m, 2m, Fixtures.Now.AddMinutes(-39));
        _attempts.ListSubmittedByQuestionIdAsync(_capital.Id, Arg.Any<CancellationToken>()).Returns([attempt]);
        return attempt;
    }

    [Fact]
    public async Task ATypedAnswerJudgedAgainstTheOldKey_IsJudgedAgain_AndTheScoreWaitsForTheNewVerdict()
    {
        var attempt = SubmittedWithAJudgedAnswer("The French capital is Paris.", TypedAnswerVerdict.Correct);

        var changed = await Rescorer(_evaluator).RescoreForQuestionAsync(_capital.Id, "The key was corrected.", CancellationToken.None);

        // Not rescored yet: the score is the one the candidate was shown until the new verdict says otherwise.
        Assert.Equal(0, changed);
        Assert.Equal(2m, attempt.Score);
        Assert.Empty(attempt.Revisions);
        Assert.Equal(AttemptStatus.Submitted, attempt.Status);
        Assert.True(attempt.IsAwaitingEvaluation);
        Assert.Equal("The key was corrected.", attempt.PendingRevisionReason);
        // The old verdict stays in force until the new one replaces it.
        Assert.Equal(TypedAnswerVerdict.Correct, attempt.Answers.Single().TextVerdict);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnAttemptStillBeingGraded_HasTheAnswerAskedAboutAgain_AndStaysInGrading()
    {
        var attempt = Attempt.Start(_exam.Id, Guid.NewGuid(), 1, Fixtures.Now.AddHours(-1), Fixtures.Now.AddHours(1));
        attempt.RecordTextAnswer(_capital.Id, "The French capital is Paris.", Fixtures.Now.AddMinutes(-50));
        attempt.SubmitForGrading(Fixtures.Now.AddMinutes(-40), [_capital.Id]);
        var firstQueued = attempt.Answers.Single().EvaluationRequestedAtUtc;
        _attempts.ListSubmittedByQuestionIdAsync(_capital.Id, Arg.Any<CancellationToken>()).Returns([attempt]);

        var changed = await Rescorer(_evaluator).RescoreForQuestionAsync(_capital.Id, "The key was corrected.", CancellationToken.None);

        Assert.Equal(0, changed);
        Assert.Equal(AttemptStatus.Grading, attempt.Status);
        Assert.Null(attempt.Score);
        // Queued again, so a verdict being asked for against the old key is not kept.
        Assert.Equal(Fixtures.Now, attempt.Answers.Single().EvaluationRequestedAtUtc);
        Assert.NotEqual(firstQueued, attempt.Answers.Single().EvaluationRequestedAtUtc);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnAttemptStillBeingGraded_IsLeftForTheWorkerToScore_WhenTheCorrectedQuestionNeedsNoJudging()
    {
        // The candidate typed what the corrected key accepts, so the evaluator has nothing to say about it. The attempt has no score yet to revise.
        var attempt = Attempt.Start(_exam.Id, Guid.NewGuid(), 1, Fixtures.Now.AddHours(-1), Fixtures.Now.AddHours(1));
        attempt.RecordTextAnswer(_capital.Id, "Lyon", Fixtures.Now.AddMinutes(-50));
        attempt.SubmitForGrading(Fixtures.Now.AddMinutes(-40), [_capital.Id]);
        _attempts.ListSubmittedByQuestionIdAsync(_capital.Id, Arg.Any<CancellationToken>()).Returns([attempt]);

        var changed = await Rescorer(_evaluator).RescoreForQuestionAsync(_capital.Id, "The key was corrected.", CancellationToken.None);

        Assert.Equal(0, changed);
        Assert.Equal(AttemptStatus.Grading, attempt.Status);
        Assert.Null(attempt.Score);
    }

    [Fact]
    public async Task ATypedAnswerTheCorrectedKeyAccepts_IsRescoredAtOnce_WithoutTheEvaluator()
    {
        var attempt = SubmittedWithAJudgedAnswer("Lyon", TypedAnswerVerdict.Wrong);

        var changed = await Rescorer(_evaluator).RescoreForQuestionAsync(_capital.Id, "The key was corrected.", CancellationToken.None);

        Assert.Equal(1, changed);
        Assert.Equal(2m, attempt.Score);
        Assert.False(attempt.IsAwaitingEvaluation);
        Assert.Equal("The key was corrected.", Assert.Single(attempt.Revisions).Reason);
    }

    [Fact]
    public async Task WithNoEvaluatorSetUp_ACorrectionQueuesNothing_AndRescoresByWhatIsStored()
    {
        _evaluator.IsEnabled.Returns(false);
        var attempt = SubmittedWithAJudgedAnswer("Lyon", TypedAnswerVerdict.Wrong);
        SetKey("Paris");

        var changed = await Rescorer(_evaluator).RescoreForQuestionAsync(_capital.Id, "The key was corrected.", CancellationToken.None);

        // "Lyon" is wrong under the new key, as it was: the stored wrong verdict agrees, and nothing moves.
        Assert.Equal(0, changed);
        Assert.False(attempt.IsAwaitingEvaluation);
        Assert.Equal(-1m, attempt.Score);
    }
}

/// <summary>The <c>Gemini</c> settings are checked when the host starts, so a mistyped one stops it there and not at the first answer.</summary>
public class GeminiOptionsValidatorTests
{
    private static ValidateOptionsResult Validate(GeminiOptions options) => new GeminiOptionsValidator().Validate(Options.DefaultName, options);

    [Fact]
    public void TheDefaults_AreValid_WithNoKey() => Assert.True(Validate(new GeminiOptions()).Succeeded);

    [Fact]
    public void AMistypedModel_StopsTheHost_AndNamesTheSetting()
    {
        var result = Validate(new GeminiOptions { Model = "gemini flash" });

        Assert.True(result.Failed);
        Assert.Contains("Gemini:Model", result.FailureMessage);
    }

    [Fact]
    public void ABaseAddressThatIsNotAWebAddress_StopsTheHost()
    {
        var result = Validate(new GeminiOptions { BaseUrl = "not an address" });

        Assert.True(result.Failed);
        Assert.Contains("Gemini:BaseUrl", result.FailureMessage);
    }
}
