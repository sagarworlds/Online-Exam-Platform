using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application;
using ExamPlatform.Modules.ExamRuntime.Application.Commands;
using ExamPlatform.Modules.ExamRuntime.Application.Dtos;
using ExamPlatform.Modules.ExamRuntime.Application.Grading;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Application.Queries;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Events;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>
/// An attempt cannot wait in grading for ever: once its typed answers have waited longer than the setting allows, each is marked by the
/// accepted answers alone, says so, and the attempt is scored. An answer a worker is holding, or queued more recently, is left alone.
/// </summary>
public class TypedAnswerFallbackTests
{
    private static readonly DateTime SubmittedAt = Fixtures.Now.AddMinutes(30);

    private readonly FakeClock _clock = new(SubmittedAt.AddMinutes(31));
    private readonly IExamCatalog _catalog = Substitute.For<IExamCatalog>();
    private readonly IQuestionBank _bank = Substitute.For<IQuestionBank>();
    private readonly IAttemptRepository _attempts = Substitute.For<IAttemptRepository>();
    private readonly IExamRuntimeUnitOfWork _unitOfWork = Substitute.For<IExamRuntimeUnitOfWork>();
    private readonly IAttemptLock _hold = Substitute.For<IAttemptLock>();
    private readonly AiGradingOptions _settings = new();
    private readonly QuestionSnapshot _capital = Fixtures.TextQuestion("Paris");
    private readonly QuestionSnapshot _river = Fixtures.TextQuestion("Seine");
    private readonly QuestionSnapshot _single = Fixtures.Question();
    private readonly ExamSnapshot _exam;

    public TypedAnswerFallbackTests()
    {
        _exam = Fixtures.Exam([_capital, _river, _single], correct: 2m, incorrect: -1m);
        _catalog.FindAsync(_exam.Id, Arg.Any<CancellationToken>()).Returns(_exam);
        _bank.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<QuestionSnapshot>>(
                new[] { _capital, _river, _single }.Where(q => call.Arg<IReadOnlyCollection<Guid>>().Contains(q.Id)).ToList()));
        _unitOfWork.LockAttemptAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(_hold);
    }

    private TypedAnswerGrading Grading => new(_attempts, _catalog, _bank, _unitOfWork, new ProctorPolicy(), _clock, Options.Create(_settings));

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

    private static void Hold(AttemptAnswer answer, DateTime until) =>
        typeof(AttemptAnswer).GetProperty(nameof(AttemptAnswer.EvaluationLeaseUntilUtc))!.SetValue(answer, until);

    private static AttemptAnswer AnswerTo(Attempt attempt, QuestionSnapshot question) => attempt.Answers.Single(a => a.QuestionId == question.Id);

    // ---- finding the attempts -----------------------------------------------------------------------------------

    [Fact]
    public async Task TheAttemptsFound_AreThoseQueuedBeforeTheCutoff()
    {
        var id = Guid.NewGuid();
        _attempts.ListGradingOverdueAsync(_clock.UtcNow.AddMinutes(-30), _clock.UtcNow, 50, Arg.Any<CancellationToken>()).Returns([id]);

        var found = await Grading.FindOverdueAsync(50, CancellationToken.None);

        Assert.Equal(new[] { id }, found);
    }

    [Fact]
    public async Task WithTheWaitSetToZero_NothingIsEverOverdue_AndTheDatabaseIsNotAsked()
    {
        _settings.MaxWaitMinutes = 0;

        var found = await Grading.FindOverdueAsync(50, CancellationToken.None);

        Assert.Empty(found);
        await _attempts.DidNotReceiveWithAnyArgs().ListGradingOverdueAsync(default, default, default, default);
    }

    // ---- giving up ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task AnAnswerThatWaitedTooLong_IsMarkedByTheAcceptedAnswersAlone_SaysSo_AndTheAttemptIsScored()
    {
        var attempt = Graded(_capital);
        var answer = AnswerTo(attempt, _capital);
        // It was tried and failed on the way, which stays on the answer for staff to see.
        attempt.RecordEvaluationFailure(_capital.Id, "The AI service answered 503.", _clock.UtcNow);

        var gaveUp = await Grading.GiveUpAsync(attempt.Id, CancellationToken.None);

        Assert.Equal(1, gaveUp);
        Assert.Equal(AttemptStatus.Submitted, attempt.Status);
        // The typed answer is wrong by the accepted answers (-1) and the chosen option is right (+2), of 6.
        Assert.Equal(1m, attempt.Score);
        Assert.Equal(6m, attempt.MaxScore);
        Assert.Null(answer.TextVerdict);
        Assert.False(answer.IsAwaitingEvaluation);
        Assert.Equal(_clock.UtcNow, answer.EvaluationGaveUpAtUtc);
        Assert.Contains("30 minutes", answer.EvaluationReason);
        Assert.Contains("accepted answers alone", answer.EvaluationReason);
        Assert.Equal(1, answer.EvaluationFailures);
        Assert.Equal("The AI service answered 503.", answer.EvaluationLastError);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _hold.Received(1).CompleteAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnAnswerThatTheAcceptedAnswersAccept_IsStillRight_WhenTheAttemptIsGivenUpOn()
    {
        // The key was corrected to accept what the candidate typed while the attempt waited: the exact match wins, as it always does.
        var attempt = Graded(_capital);
        _bank.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<QuestionSnapshot>>(
                [_capital with { AcceptedAnswers = ["Paris", "Something worded differently"] }, _river, _single]));

        await Grading.GiveUpAsync(attempt.Id, CancellationToken.None);

        Assert.Equal(4m, attempt.Score);
    }

    [Fact]
    public async Task AnAnswerThatHasNotWaitedLongEnough_IsLeftToTheWorker()
    {
        _clock.UtcNow = SubmittedAt.AddMinutes(10);
        var attempt = Graded(_capital);

        var gaveUp = await Grading.GiveUpAsync(attempt.Id, CancellationToken.None);

        Assert.Equal(0, gaveUp);
        Assert.Equal(AttemptStatus.Grading, attempt.Status);
        Assert.True(attempt.IsAwaitingEvaluation);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnAnswerAWorkerIsHoldingRightNow_IsLeftToIt_UntilTheHoldLapses()
    {
        var attempt = Graded(_capital);
        var answer = AnswerTo(attempt, _capital);
        Hold(answer, _clock.UtcNow.AddMinutes(4));

        Assert.Equal(0, await Grading.GiveUpAsync(attempt.Id, CancellationToken.None));
        Assert.Equal(AttemptStatus.Grading, attempt.Status);

        // The worker died holding it: the hold ran out, and nothing is in the way any more.
        Hold(answer, _clock.UtcNow.AddMinutes(-1));
        Assert.Equal(1, await Grading.GiveUpAsync(attempt.Id, CancellationToken.None));
        Assert.Equal(AttemptStatus.Submitted, attempt.Status);
    }

    [Fact]
    public async Task WhileAnotherAnswerIsStillHeld_TheAttemptIsNotScored_AndTheOverdueOneIsGivenUp()
    {
        var attempt = Graded(_capital, _river);
        Hold(AnswerTo(attempt, _river), _clock.UtcNow.AddMinutes(4));

        var gaveUp = await Grading.GiveUpAsync(attempt.Id, CancellationToken.None);

        Assert.Equal(1, gaveUp);
        Assert.Equal(AttemptStatus.Grading, attempt.Status);
        Assert.Null(attempt.Score);
        Assert.NotNull(AnswerTo(attempt, _capital).EvaluationGaveUpAtUtc);
        Assert.Equal(_river.Id, Assert.Single(attempt.AnswersAwaitingEvaluation).QuestionId);
    }

    [Fact]
    public async Task AVerdictAlreadyGiven_IsKept_WhenAnotherAnswerIsGivenUp()
    {
        var attempt = Graded(_capital, _river);
        attempt.RecordEvaluation(_river.Id, TypedAnswerVerdict.Correct, "Says the same.", "gemini-test", SubmittedAt.AddMinutes(1));

        await Grading.GiveUpAsync(attempt.Id, CancellationToken.None);

        // The river answer is right by the verdict (+2) and the capital answer is wrong by the accepted answers (-1), with the chosen option (+2).
        Assert.Equal(3m, attempt.Score);
        Assert.Equal(TypedAnswerVerdict.Correct, AnswerTo(attempt, _river).TextVerdict);
        Assert.Null(AnswerTo(attempt, _river).EvaluationGaveUpAtUtc);
    }

    [Fact]
    public async Task WithTheWaitSetToZero_NoAnswerIsEverGivenUpOn()
    {
        _settings.MaxWaitMinutes = 0;
        _clock.UtcNow = SubmittedAt.AddDays(30);
        var attempt = Graded(_capital);

        Assert.Equal(0, await Grading.GiveUpAsync(attempt.Id, CancellationToken.None));

        Assert.Equal(AttemptStatus.Grading, attempt.Status);
    }

    [Fact]
    public async Task ASubmittedAttemptWaitingForARecheck_IsNeverGivenUpOn_ItKeepsItsScoreMeanwhile()
    {
        var attempt = Attempt.Start(_exam.Id, Guid.NewGuid(), 1, Fixtures.Now, Fixtures.Now.AddHours(1));
        attempt.RecordTextAnswer(_capital.Id, "Something worded differently", Fixtures.Now.AddMinutes(1));
        attempt.Submit(SubmittedAt, score: -1m, maxScore: 6m);
        attempt.RequestReevaluation([_capital.Id], "Corrected.", SubmittedAt);
        _attempts.GetByIdAsync(attempt.Id, Arg.Any<CancellationToken>()).Returns(attempt);

        Assert.Equal(0, await Grading.GiveUpAsync(attempt.Id, CancellationToken.None));

        Assert.True(attempt.IsAwaitingEvaluation);
        Assert.Equal(-1m, attempt.Score);
    }
}

/// <summary>The rules of having a typed answer checked by the AI evaluator on staff's request, and what that does to the answer.</summary>
public class AttemptAiCheckTests
{
    private readonly QuestionSnapshot _capital = Fixtures.TextQuestion("Paris");

    private Attempt Submitted(string typed = "Something worded differently")
    {
        var attempt = Attempt.Start(Guid.NewGuid(), Guid.NewGuid(), 1, Fixtures.Now, Fixtures.Now.AddHours(1));
        attempt.RecordTextAnswer(_capital.Id, typed, Fixtures.Now.AddMinutes(1));
        attempt.Submit(Fixtures.Now.AddMinutes(30), score: 0m, maxScore: 1m);
        return attempt;
    }

    private static T Raised<T>(Attempt attempt) where T : class => Assert.IsType<T>(attempt.DomainEvents.Single(e => e is T));

    [Fact]
    public void ACheckOnASubmittedAttempt_QueuesTheAnswer_KeepsTheScore_AndSaysWhyItMayChange()
    {
        var attempt = Submitted();

        attempt.RequestAiCheck(_capital.Id, "Checked again.", Fixtures.Now.AddHours(2));

        Assert.True(attempt.IsAwaitingEvaluation);
        Assert.Equal(AttemptStatus.Submitted, attempt.Status);
        Assert.Equal(0m, attempt.Score);
        Assert.Equal("Checked again.", attempt.PendingRevisionReason);
        Assert.Equal(_capital.Id, Raised<AttemptAiCheckRequestedEvent>(attempt).QuestionId);
    }

    [Fact]
    public void ACheckOnAnAttemptStillInGrading_QueuesAnAnswerThatWasJudged_WithNothingToRevise()
    {
        var river = Fixtures.TextQuestion("Seine");
        var attempt = Attempt.Start(Guid.NewGuid(), Guid.NewGuid(), 1, Fixtures.Now, Fixtures.Now.AddHours(1));
        attempt.RecordTextAnswer(_capital.Id, "Something worded differently", Fixtures.Now.AddMinutes(1));
        attempt.RecordTextAnswer(river.Id, "Another wording", Fixtures.Now.AddMinutes(1));
        attempt.SubmitForGrading(Fixtures.Now.AddMinutes(30), [_capital.Id, river.Id]);
        // One answer has its verdict and the other is still waiting, so the attempt is still in grading.
        attempt.RecordEvaluation(_capital.Id, TypedAnswerVerdict.Wrong, "Different.", "gemini-test", Fixtures.Now.AddMinutes(31));

        attempt.RequestAiCheck(_capital.Id, "Checked again.", Fixtures.Now.AddHours(2));

        Assert.Equal(AttemptStatus.Grading, attempt.Status);
        Assert.Null(attempt.PendingRevisionReason);
        var checkedAgain = attempt.Answers.Single(a => a.QuestionId == _capital.Id);
        Assert.Equal(Fixtures.Now.AddHours(2), checkedAgain.EvaluationRequestedAtUtc);
        // The earlier verdict stands until the new one replaces it.
        Assert.Equal(TypedAnswerVerdict.Wrong, checkedAgain.TextVerdict);
    }

    [Fact]
    public void AnAnswerAlreadyWaitingInAnAttemptInGrading_IsLeftAsItIs()
    {
        var attempt = Attempt.Start(Guid.NewGuid(), Guid.NewGuid(), 1, Fixtures.Now, Fixtures.Now.AddHours(1));
        attempt.RecordTextAnswer(_capital.Id, "Something worded differently", Fixtures.Now.AddMinutes(1));
        attempt.SubmitForGrading(Fixtures.Now.AddMinutes(30), [_capital.Id]);

        attempt.RequestAiCheck(_capital.Id, "Checked again.", Fixtures.Now.AddHours(2));

        Assert.Equal(Fixtures.Now.AddMinutes(30), Assert.Single(attempt.Answers).EvaluationRequestedAtUtc);
    }

    [Fact]
    public void AnAnswerThePlatformGaveUpOn_CanBeCheckedLater_AndTheNoteIsClearedWhileItWaits()
    {
        var attempt = Attempt.Start(Guid.NewGuid(), Guid.NewGuid(), 1, Fixtures.Now, Fixtures.Now.AddHours(1));
        attempt.RecordTextAnswer(_capital.Id, "Something worded differently", Fixtures.Now.AddMinutes(1));
        attempt.SubmitForGrading(Fixtures.Now.AddMinutes(30), [_capital.Id]);
        attempt.GiveUpEvaluations(Fixtures.Now.AddHours(1), "Gave up.", Fixtures.Now.AddHours(1));
        attempt.ApplyEvaluatedScore(-1m, 1m, Fixtures.Now.AddHours(1));
        var answer = Assert.Single(attempt.Answers);
        Assert.NotNull(answer.EvaluationGaveUpAtUtc);

        attempt.RequestAiCheck(_capital.Id, "Checked again.", Fixtures.Now.AddHours(3));

        Assert.True(answer.IsAwaitingEvaluation);
        Assert.Null(answer.EvaluationGaveUpAtUtc);

        attempt.RecordEvaluation(_capital.Id, TypedAnswerVerdict.Correct, "Same.", "gemini-test", Fixtures.Now.AddHours(3));

        Assert.Equal(TypedAnswerVerdict.Correct, answer.TextVerdict);
        Assert.Null(answer.EvaluationGaveUpAtUtc);
    }

    [Fact]
    public void AskingAgainWhileTheAnswerIsWaiting_ChangesNothing_AndRaisesNoSecondEvent()
    {
        var attempt = Submitted();
        attempt.RequestAiCheck(_capital.Id, "Checked again.", Fixtures.Now.AddHours(2));
        attempt.ClearDomainEvents();

        attempt.RequestAiCheck(_capital.Id, "Checked again.", Fixtures.Now.AddHours(3));

        // Still queued from the first time, so a worker already holding it is not made to throw its verdict away.
        Assert.Equal(Fixtures.Now.AddHours(2), Assert.Single(attempt.Answers).EvaluationRequestedAtUtc);
        Assert.Empty(attempt.DomainEvents);
    }

    [Fact]
    public void AnOpenAttempt_CannotHaveAnAnswerChecked()
    {
        var attempt = Attempt.Start(Guid.NewGuid(), Guid.NewGuid(), 1, Fixtures.Now, Fixtures.Now.AddHours(1));
        attempt.RecordTextAnswer(_capital.Id, "Something worded differently", Fixtures.Now.AddMinutes(1));

        Assert.Throws<AttemptNotSubmittedError>(() => attempt.RequestAiCheck(_capital.Id, "Checked again.", Fixtures.Now.AddHours(2)));
    }

    [Fact]
    public void AnInvalidatedResult_CannotHaveAnAnswerChecked()
    {
        var attempt = Submitted();
        attempt.Invalidate(Guid.NewGuid(), "Cheating.", Fixtures.Now.AddHours(1));

        Assert.Throws<InvalidAttemptError>(() => attempt.RequestAiCheck(_capital.Id, "Checked again.", Fixtures.Now.AddHours(2)));
        Assert.False(attempt.IsAwaitingEvaluation);
    }

    [Fact]
    public void AQuestionTheCandidateDidNotType_CannotBeChecked()
    {
        var attempt = Submitted();

        Assert.Throws<InvalidAttemptError>(() => attempt.RequestAiCheck(Guid.NewGuid(), "Checked again.", Fixtures.Now.AddHours(2)));
        Assert.False(attempt.IsAwaitingEvaluation);
    }
}

/// <summary>The check staff ask for from the paper: who may ask about which answer, and what is queued.</summary>
public class RequestAiCheckHandlerTests
{
    private readonly Guid _candidate = Guid.NewGuid();
    private readonly FakeClock _clock = new(Fixtures.Now.AddHours(2));
    private readonly IExamCatalog _catalog = Substitute.For<IExamCatalog>();
    private readonly IQuestionBank _bank = Substitute.For<IQuestionBank>();
    private readonly IAttemptRepository _attempts = Substitute.For<IAttemptRepository>();
    private readonly IExamRuntimeUnitOfWork _unitOfWork = Substitute.For<IExamRuntimeUnitOfWork>();
    private readonly IAttemptLock _hold = Substitute.For<IAttemptLock>();
    private readonly ITextAnswerEvaluator _evaluator = Substitute.For<ITextAnswerEvaluator>();
    private readonly QuestionSnapshot _capital = Fixtures.TextQuestion("Paris");
    private readonly QuestionSnapshot _single = Fixtures.Question();
    private readonly ExamSnapshot _exam;

    public RequestAiCheckHandlerTests()
    {
        _exam = Fixtures.Exam([_capital, _single], correct: 2m, incorrect: -1m);
        _catalog.FindAsync(_exam.Id, Arg.Any<CancellationToken>()).Returns(_exam);
        _bank.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<QuestionSnapshot>>(
                new[] { _capital, _single }.Where(q => call.Arg<IReadOnlyCollection<Guid>>().Contains(q.Id)).ToList()));
        _evaluator.IsEnabled.Returns(true);
        _unitOfWork.LockAttemptAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(_hold);
    }

    private RequestAiCheckHandler Handler
    {
        get
        {
            var closer = new AttemptCloser(_bank, _unitOfWork, _clock);
            var access = new StaffAttemptAccess(_attempts, _catalog, new AttemptAccess(_attempts, _catalog, closer, _clock, new ProctorPolicy()), new ProctorPolicy());
            return new RequestAiCheckHandler(access, _bank, _evaluator, _unitOfWork, _clock);
        }
    }

    private Attempt Submitted(string typed)
    {
        var attempt = Attempt.Start(_exam.Id, _candidate, 1, Fixtures.Now, Fixtures.Now.AddHours(1));
        attempt.RecordTextAnswer(_capital.Id, typed, Fixtures.Now.AddMinutes(1));
        attempt.RecordAnswer(_single.Id, _single.Correct(), Fixtures.Now.AddMinutes(2));
        attempt.Submit(Fixtures.Now.AddMinutes(30), score: 1m, maxScore: 4m);
        _attempts.GetByIdAsync(attempt.Id, Arg.Any<CancellationToken>()).Returns(attempt);
        return attempt;
    }

    [Fact]
    public async Task ACheck_QueuesTheAnswer_KeepsTheScore_AndCommitsWhatWasSaved()
    {
        var attempt = Submitted("Something worded differently");

        var summary = await Handler.HandleAsync(_exam.Id, attempt.Id, _capital.Id, CancellationToken.None);

        Assert.True(attempt.IsAwaitingEvaluation);
        Assert.Equal(1m, attempt.Score);
        Assert.Equal(RequestAiCheckHandler.Reason, attempt.PendingRevisionReason);
        Assert.Equal(AttemptStatus.Submitted, summary.Status);
        await _unitOfWork.Received(1).LockAttemptAsync(attempt.Id, true, Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _hold.Received(1).CompleteAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithNoEvaluatorSetUp_ThereIsNothingToCheckWith()
    {
        _evaluator.IsEnabled.Returns(false);
        var attempt = Submitted("Something worded differently");

        await Assert.ThrowsAsync<TextEvaluatorNotSetUpError>(() => Handler.HandleAsync(_exam.Id, attempt.Id, _capital.Id, CancellationToken.None));

        Assert.False(attempt.IsAwaitingEvaluation);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnAnswerThatMatchesAnAcceptedAnswer_NeedsNoCheck()
    {
        var attempt = Submitted("  paris ");

        var error = await Assert.ThrowsAsync<InvalidAttemptError>(() => Handler.HandleAsync(_exam.Id, attempt.Id, _capital.Id, CancellationToken.None));

        Assert.Contains("word for word", error.Message);
        Assert.False(attempt.IsAwaitingEvaluation);
    }

    [Fact]
    public async Task AQuestionAnsweredByChoosingAnOption_CannotBeChecked()
    {
        var attempt = Submitted("Something worded differently");

        await Assert.ThrowsAsync<InvalidAttemptError>(() => Handler.HandleAsync(_exam.Id, attempt.Id, _single.Id, CancellationToken.None));

        Assert.False(attempt.IsAwaitingEvaluation);
    }

    [Fact]
    public async Task AQuestionNotOnThePaper_CannotBeChecked()
    {
        var attempt = Submitted("Something worded differently");

        await Assert.ThrowsAsync<QuestionNotInAttemptError>(() => Handler.HandleAsync(_exam.Id, attempt.Id, Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task AnAttemptOfAnotherExam_AnswersLikeAMissingOne()
    {
        var attempt = Submitted("Something worded differently");

        await Assert.ThrowsAsync<AttemptNotFoundError>(() => Handler.HandleAsync(Guid.NewGuid(), attempt.Id, _capital.Id, CancellationToken.None));
    }

    [Fact]
    public async Task AnOpenAttempt_CannotBeChecked()
    {
        var attempt = Attempt.Start(_exam.Id, _candidate, 1, Fixtures.Now.AddHours(1), Fixtures.Now.AddHours(5));
        attempt.RecordTextAnswer(_capital.Id, "Something worded differently", Fixtures.Now.AddHours(1));
        _attempts.GetByIdAsync(attempt.Id, Arg.Any<CancellationToken>()).Returns(attempt);

        await Assert.ThrowsAsync<AttemptNotSubmittedError>(() => Handler.HandleAsync(_exam.Id, attempt.Id, _capital.Id, CancellationToken.None));

        Assert.False(attempt.IsAwaitingEvaluation);
    }
}

/// <summary>What the paper shows staff about the AI evaluator's part in each typed answer, and whether they can ask for a check.</summary>
public class AttemptPaperAiCheckTests
{
    private readonly IExamCatalog _catalog = Substitute.For<IExamCatalog>();
    private readonly IQuestionBank _bank = Substitute.For<IQuestionBank>();
    private readonly IAttemptRepository _attempts = Substitute.For<IAttemptRepository>();
    private readonly ITextAnswerEvaluator _evaluator = Substitute.For<ITextAnswerEvaluator>();
    private readonly QuestionSnapshot _capital = Fixtures.TextQuestion("Paris");
    private readonly QuestionSnapshot _single = Fixtures.Question();
    private readonly ExamSnapshot _exam;

    public AttemptPaperAiCheckTests()
    {
        _exam = Fixtures.Exam([_capital, _single], correct: 2m, incorrect: -1m);
        _catalog.FindAsync(_exam.Id, Arg.Any<CancellationToken>()).Returns(_exam);
        _bank.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<QuestionSnapshot>>(
                new[] { _capital, _single }.Where(q => call.Arg<IReadOnlyCollection<Guid>>().Contains(q.Id)).ToList()));
        _evaluator.IsEnabled.Returns(true);
    }

    private GetAttemptPaperHandler Handler(ITextAnswerEvaluator? evaluator) =>
        new(_catalog, _attempts, _bank, new ProctorPolicy(), evaluator);

    private Attempt Open(string typed)
    {
        var attempt = Attempt.Start(_exam.Id, Guid.NewGuid(), 1, Fixtures.Now, Fixtures.Now.AddHours(1));
        attempt.RecordTextAnswer(_capital.Id, typed, Fixtures.Now.AddMinutes(1));
        attempt.RecordAnswer(_single.Id, _single.Correct(), Fixtures.Now.AddMinutes(2));
        _attempts.GetByIdAsync(attempt.Id, Arg.Any<CancellationToken>()).Returns(attempt);
        return attempt;
    }

    private async Task<(AttemptPaperDto Paper, AttemptPaperQuestionDto Typed)> ReadAsync(Attempt attempt, ITextAnswerEvaluator? evaluator = null)
    {
        var paper = await Handler(evaluator ?? _evaluator).HandleAsync(_exam.Id, attempt.Id, CancellationToken.None);
        return (paper, paper.Sections.SelectMany(s => s.Questions).Single(q => q.Id == _capital.Id));
    }

    [Fact]
    public async Task AVerdictTheEvaluatorGave_IsShown_WithItsReasonAndModel()
    {
        var attempt = Open("Something worded differently");
        attempt.SubmitForGrading(Fixtures.Now.AddMinutes(30), [_capital.Id]);
        attempt.RecordEvaluation(_capital.Id, TypedAnswerVerdict.Correct, "Says the same.", "gemini-test", Fixtures.Now.AddMinutes(31));
        attempt.ApplyEvaluatedScore(4m, 4m, Fixtures.Now.AddMinutes(31));

        var (paper, typed) = await ReadAsync(attempt);

        var check = typed.AiCheck;
        Assert.NotNull(check);
        Assert.Equal(AiCheckState.Judged, check.State);
        Assert.Equal(TypedAnswerVerdict.Correct, check.Verdict);
        Assert.Equal("Says the same.", check.Reason);
        Assert.Equal("gemini-test", check.Model);
        Assert.Equal(Fixtures.Now.AddMinutes(31), check.AtUtc);
        Assert.True(paper.AiCheckAvailable);
        // The question's marks already count the verdict.
        Assert.Equal(AnswerVerdict.Correct, typed.Verdict);
    }

    [Fact]
    public async Task AnAnswerStillWaiting_IsShownWaiting_WithWhatWentWrongSoFar_EvenWhileTheAttemptIsInGrading()
    {
        var attempt = Open("Something worded differently");
        attempt.SubmitForGrading(Fixtures.Now.AddMinutes(30), [_capital.Id]);
        attempt.RecordEvaluationFailure(_capital.Id, "The AI service answered 503.", Fixtures.Now.AddMinutes(32));

        var (paper, typed) = await ReadAsync(attempt);

        Assert.Equal(AiCheckState.Waiting, typed.AiCheck!.State);
        Assert.Equal(1, typed.AiCheck.Failures);
        Assert.Equal("The AI service answered 503.", typed.AiCheck.LastError);
        Assert.Equal(AttemptStatus.Grading, paper.Status);
        Assert.True(paper.AiCheckAvailable);
    }

    [Fact]
    public async Task AnAnswerThePlatformGaveUpOn_SaysSo()
    {
        var attempt = Open("Something worded differently");
        attempt.SubmitForGrading(Fixtures.Now.AddMinutes(30), [_capital.Id]);
        attempt.GiveUpEvaluations(Fixtures.Now.AddHours(1), "No verdict within 30 minutes.", Fixtures.Now.AddHours(1));
        attempt.ApplyEvaluatedScore(1m, 4m, Fixtures.Now.AddHours(1));

        var (_, typed) = await ReadAsync(attempt);

        Assert.Equal(AiCheckState.GaveUp, typed.AiCheck!.State);
        Assert.Null(typed.AiCheck.Verdict);
        Assert.Equal("No verdict within 30 minutes.", typed.AiCheck.Reason);
        Assert.Equal(Fixtures.Now.AddHours(1), typed.AiCheck.AtUtc);
    }

    [Fact]
    public async Task AnAnswerNoOneChecked_IsShownNotChecked_AndCanBeChecked()
    {
        var attempt = Open("Something worded differently");
        attempt.Submit(Fixtures.Now.AddMinutes(30), score: 1m, maxScore: 4m);

        var (paper, typed) = await ReadAsync(attempt);

        Assert.Equal(AiCheckState.NotChecked, typed.AiCheck!.State);
        Assert.True(paper.AiCheckAvailable);
    }

    [Fact]
    public async Task AnExactAnswer_ShowsNothingOfTheEvaluator()
    {
        var attempt = Open("  paris ");
        attempt.Submit(Fixtures.Now.AddMinutes(30), score: 4m, maxScore: 4m);

        var (_, typed) = await ReadAsync(attempt);

        Assert.Null(typed.AiCheck);
    }

    [Fact]
    public async Task AnOpenAttempt_ShowsNothingOfTheEvaluator_AndOffersNoCheck()
    {
        var attempt = Open("Something worded differently");

        var (paper, typed) = await ReadAsync(attempt);

        Assert.Null(typed.AiCheck);
        Assert.False(paper.AiCheckAvailable);
    }

    [Fact]
    public async Task WithNoEvaluatorSetUp_NoCheckIsOffered_ButWhatWasJudgedStillShows()
    {
        var attempt = Open("Something worded differently");
        attempt.SubmitForGrading(Fixtures.Now.AddMinutes(30), [_capital.Id]);
        attempt.RecordEvaluation(_capital.Id, TypedAnswerVerdict.Wrong, "Different.", "gemini-test", Fixtures.Now.AddMinutes(31));
        attempt.ApplyEvaluatedScore(1m, 4m, Fixtures.Now.AddMinutes(31));
        var off = Substitute.For<ITextAnswerEvaluator>();
        off.IsEnabled.Returns(false);

        var (paper, typed) = await ReadAsync(attempt, off);

        Assert.False(paper.AiCheckAvailable);
        Assert.Equal(AiCheckState.Judged, typed.AiCheck!.State);
    }

    [Fact]
    public async Task AnInvalidatedResult_OffersNoCheck()
    {
        var attempt = Open("Something worded differently");
        attempt.Submit(Fixtures.Now.AddMinutes(30), score: 1m, maxScore: 4m);
        attempt.Invalidate(Guid.NewGuid(), "Cheating.", Fixtures.Now.AddHours(1));

        var (paper, _) = await ReadAsync(attempt);

        Assert.False(paper.AiCheckAvailable);
    }
}
