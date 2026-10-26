using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application;
using ExamPlatform.Modules.ExamRuntime.Application.Commands;
using ExamPlatform.Modules.ExamRuntime.Application.Dtos;
using ExamPlatform.Modules.ExamRuntime.Application.Grading;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;
using NSubstitute;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>
/// Which typed answers a bulk recheck sends: the ones the paper labels "Not checked" or "Not judged by the AI", and nothing the evaluator
/// already judged, is judging now, or has no reason to judge.
/// </summary>
public class TypedAnswerChecksVerdictTests
{
    private const string Worded = "Something worded differently";

    private readonly QuestionSnapshot _neverAsked = Fixtures.TextQuestion("Paris");
    private readonly QuestionSnapshot _gaveUp = Fixtures.TextQuestion("Seine");
    private readonly QuestionSnapshot _judged = Fixtures.TextQuestion("Rhone");
    private readonly QuestionSnapshot _waiting = Fixtures.TextQuestion("Loire");
    private readonly QuestionSnapshot _exactMatch = Fixtures.TextQuestion("Garonne");
    private readonly QuestionSnapshot _unanswered = Fixtures.TextQuestion("Marne");
    private readonly QuestionSnapshot _chosen = Fixtures.Question();

    private IReadOnlyDictionary<Guid, QuestionSnapshot> Questions =>
        new[] { _neverAsked, _gaveUp, _judged, _waiting, _exactMatch, _unanswered, _chosen }.ToDictionary(q => q.Id);

    // One attempt with a typed answer in each state the paper can show.
    private Attempt AttemptInEveryState()
    {
        var attempt = Attempt.Start(Guid.NewGuid(), Guid.NewGuid(), 1, Fixtures.Now, Fixtures.Now.AddHours(1));
        foreach (var question in new[] { _neverAsked, _gaveUp, _judged, _waiting })
            attempt.RecordTextAnswer(question.Id, Worded, Fixtures.Now.AddMinutes(1));
        attempt.RecordTextAnswer(_exactMatch.Id, "  garonne ", Fixtures.Now.AddMinutes(1));
        // The domain refuses a blank typed answer, so a question left unanswered is the only blank there can be: it has no answer at all.
        attempt.RecordAnswer(_chosen.Id, _chosen.Correct(), Fixtures.Now.AddMinutes(2));

        // Three are queued for the evaluator; the first was never asked about.
        attempt.SubmitForGrading(Fixtures.Now.AddMinutes(30), [_gaveUp.Id, _judged.Id, _waiting.Id]);
        attempt.RecordEvaluation(_judged.Id, TypedAnswerVerdict.Wrong, "Different.", "test-model", Fixtures.Now.AddMinutes(31));
        // The platform stops waiting for the two still queued, then one of them is sent again and so is waiting once more.
        attempt.GiveUpEvaluations(Fixtures.Now.AddHours(1), "Gave up.", Fixtures.Now.AddHours(1));
        attempt.ApplyEvaluatedScore(0m, 4m, Fixtures.Now.AddHours(1));
        attempt.RequestAiCheck(_waiting.Id, "Checked again.", Fixtures.Now.AddHours(2));
        return attempt;
    }

    [Fact]
    public void TheStates_AreTheOnesThePaperShows()
    {
        var attempt = AttemptInEveryState();

        AiCheckState StateOfAnswerTo(QuestionSnapshot q) => TypedAnswerChecks.StateOf(attempt.Answers.Single(a => a.QuestionId == q.Id));

        Assert.Equal(AiCheckState.NotChecked, StateOfAnswerTo(_neverAsked));
        Assert.Equal(AiCheckState.GaveUp, StateOfAnswerTo(_gaveUp));
        Assert.Equal(AiCheckState.Judged, StateOfAnswerTo(_judged));
        Assert.Equal(AiCheckState.Waiting, StateOfAnswerTo(_waiting));
    }

    [Fact]
    public void OnlyAnswersNeverAskedAbout_OrGivenUpOn_AreSent()
    {
        var attempt = AttemptInEveryState();

        var sent = TypedAnswerChecks.QuestionsWithoutVerdict(attempt, Questions);

        // Not the judged answer, the one waiting, the exact match, the unanswered question, or the question answered by choosing an option.
        Assert.Equal(new[] { _neverAsked.Id, _gaveUp.Id }.OrderBy(id => id), sent.OrderBy(id => id));
    }

    [Fact]
    public void WhenEveryAnswerHasAVerdictOrIsWaiting_NothingIsSent()
    {
        var attempt = AttemptInEveryState();
        attempt.RequestAiCheck(_neverAsked.Id, "Checked again.", Fixtures.Now.AddHours(2));
        attempt.RequestAiCheck(_gaveUp.Id, "Checked again.", Fixtures.Now.AddHours(2));

        Assert.Empty(TypedAnswerChecks.QuestionsWithoutVerdict(attempt, Questions));
    }

    [Fact]
    public void AQuestionTheExamNoLongerHas_IsNotSent()
    {
        var attempt = AttemptInEveryState();

        Assert.Empty(TypedAnswerChecks.QuestionsWithoutVerdict(attempt, new Dictionary<Guid, QuestionSnapshot>()));
    }
}

/// <summary>The recheck staff ask for from an exam's attempts page or from one paper: what is queued, what is left alone, and what is refused.</summary>
public class RequestAiRecheckHandlerTests
{
    private const string Worded = "Something worded differently";

    private readonly FakeClock _clock = new(Fixtures.Now.AddHours(2));
    private readonly IExamCatalog _catalog = Substitute.For<IExamCatalog>();
    private readonly IQuestionBank _bank = Substitute.For<IQuestionBank>();
    private readonly IAttemptRepository _attempts = Substitute.For<IAttemptRepository>();
    private readonly IExamRuntimeUnitOfWork _unitOfWork = Substitute.For<IExamRuntimeUnitOfWork>();
    private readonly IAttemptLock _hold = Substitute.For<IAttemptLock>();
    private readonly ITextAnswerEvaluator _evaluator = Substitute.For<ITextAnswerEvaluator>();
    private readonly QuestionSnapshot _capital = Fixtures.TextQuestion("Paris");
    private readonly QuestionSnapshot _river = Fixtures.TextQuestion("Seine");
    private readonly QuestionSnapshot _single = Fixtures.Question();
    private readonly ExamSnapshot _exam;

    public RequestAiRecheckHandlerTests()
    {
        _exam = Fixtures.Exam([_capital, _river, _single], correct: 2m, incorrect: -1m);
        _catalog.FindAsync(_exam.Id, Arg.Any<CancellationToken>()).Returns(_exam);
        _bank.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<QuestionSnapshot>>(
                new[] { _capital, _river, _single }.Where(q => call.Arg<IReadOnlyCollection<Guid>>().Contains(q.Id)).ToList()));
        _evaluator.IsEnabled.Returns(true);
        _unitOfWork.LockAttemptAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(_hold);
    }

    private RequestAiRecheckHandler Handler
    {
        get
        {
            var closer = new AttemptCloser(_bank, _unitOfWork, _clock);
            var access = new StaffAttemptAccess(_attempts, _catalog, new AttemptAccess(_attempts, _catalog, closer, _clock, new ProctorPolicy()), new ProctorPolicy());
            return new RequestAiRecheckHandler(access, _attempts, _bank, _evaluator, _unitOfWork, _clock);
        }
    }

    // A submitted attempt that typed the given answers to the two text questions, none of them yet sent to the evaluator.
    private Attempt Submitted(string? capital, string? river = null)
    {
        var attempt = Attempt.Start(_exam.Id, Guid.NewGuid(), 1, Fixtures.Now, Fixtures.Now.AddHours(1));
        if (capital is not null)
            attempt.RecordTextAnswer(_capital.Id, capital, Fixtures.Now.AddMinutes(1));
        if (river is not null)
            attempt.RecordTextAnswer(_river.Id, river, Fixtures.Now.AddMinutes(1));
        attempt.RecordAnswer(_single.Id, _single.Correct(), Fixtures.Now.AddMinutes(2));
        attempt.Submit(Fixtures.Now.AddMinutes(30), score: 2m, maxScore: 6m);
        _attempts.GetByIdAsync(attempt.Id, Arg.Any<CancellationToken>()).Returns(attempt);
        return attempt;
    }

    private void Lists(params Attempt[] attempts) =>
        _attempts.ListOverIdsForExamAsync(_exam.Id, Arg.Any<CancellationToken>()).Returns(attempts.Select(a => a.Id).ToList());

    private static AttemptAnswer AnswerTo(Attempt attempt, QuestionSnapshot question) => attempt.Answers.Single(a => a.QuestionId == question.Id);

    // ---- one paper -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task OnePaper_QueuesEveryAnswerWithNoVerdict_KeepsTheScore_AndCommitsWhatWasSaved()
    {
        var attempt = Submitted(Worded, "Another wording");

        var result = await Handler.HandleForAttemptAsync(_exam.Id, attempt.Id, CancellationToken.None);

        Assert.Equal(new AiRecheckResultDto(1, 2), result);
        Assert.True(AnswerTo(attempt, _capital).IsAwaitingEvaluation);
        Assert.True(AnswerTo(attempt, _river).IsAwaitingEvaluation);
        Assert.Equal(2m, attempt.Score);
        Assert.Equal(AttemptStatus.Submitted, attempt.Status);
        Assert.Equal(RequestAiCheckHandler.Reason, attempt.PendingRevisionReason);
        await _unitOfWork.Received(1).LockAttemptAsync(attempt.Id, true, Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _hold.Received(1).CompleteAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnePaper_LeavesAnAnswerTheEvaluatorAlreadyJudged_AndSendsTheOther()
    {
        var attempt = Submitted(Worded, "Another wording");
        attempt.RequestAiCheck(_capital.Id, "Checked again.", Fixtures.Now.AddHours(1));
        attempt.RecordEvaluation(_capital.Id, TypedAnswerVerdict.Correct, "Same.", "test-model", Fixtures.Now.AddHours(1));
        attempt.ClearDomainEvents();

        var result = await Handler.HandleForAttemptAsync(_exam.Id, attempt.Id, CancellationToken.None);

        Assert.Equal(new AiRecheckResultDto(1, 1), result);
        var judged = AnswerTo(attempt, _capital);
        Assert.False(judged.IsAwaitingEvaluation);
        Assert.Equal(TypedAnswerVerdict.Correct, judged.TextVerdict);
        Assert.True(AnswerTo(attempt, _river).IsAwaitingEvaluation);
    }

    [Fact]
    public async Task OnePaper_WithNothingToSend_QueuesAndSavesNothing()
    {
        // The only typed answer matches an accepted answer, and the other question was left blank.
        var attempt = Submitted("  paris ");

        var result = await Handler.HandleForAttemptAsync(_exam.Id, attempt.Id, CancellationToken.None);

        Assert.Equal(new AiRecheckResultDto(0, 0), result);
        Assert.False(attempt.IsAwaitingEvaluation);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnePaper_StillBeingGraded_SendsTheAnswerNotYetAsked_WithNothingToRevise()
    {
        // One typed answer is queued and the attempt waits for it; the other was never asked about.
        var attempt = Attempt.Start(_exam.Id, Guid.NewGuid(), 1, Fixtures.Now, Fixtures.Now.AddHours(1));
        attempt.RecordTextAnswer(_capital.Id, Worded, Fixtures.Now.AddMinutes(1));
        attempt.RecordTextAnswer(_river.Id, "Another wording", Fixtures.Now.AddMinutes(1));
        attempt.SubmitForGrading(Fixtures.Now.AddMinutes(30), [_capital.Id]);
        _attempts.GetByIdAsync(attempt.Id, Arg.Any<CancellationToken>()).Returns(attempt);

        var result = await Handler.HandleForAttemptAsync(_exam.Id, attempt.Id, CancellationToken.None);

        Assert.Equal(new AiRecheckResultDto(1, 1), result);
        Assert.Equal(AttemptStatus.Grading, attempt.Status);
        Assert.Null(attempt.PendingRevisionReason);
        Assert.True(AnswerTo(attempt, _river).IsAwaitingEvaluation);
    }

    [Fact]
    public async Task OnePaper_WithNoEvaluatorSetUp_ThereIsNothingToCheckWith()
    {
        _evaluator.IsEnabled.Returns(false);
        var attempt = Submitted(Worded);

        await Assert.ThrowsAsync<TextEvaluatorNotSetUpError>(() => Handler.HandleForAttemptAsync(_exam.Id, attempt.Id, CancellationToken.None));

        Assert.False(attempt.IsAwaitingEvaluation);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnePaper_AnOpenAttempt_CannotBeRechecked()
    {
        var attempt = Attempt.Start(_exam.Id, Guid.NewGuid(), 1, Fixtures.Now.AddHours(1), Fixtures.Now.AddHours(5));
        attempt.RecordTextAnswer(_capital.Id, Worded, Fixtures.Now.AddHours(1));
        _attempts.GetByIdAsync(attempt.Id, Arg.Any<CancellationToken>()).Returns(attempt);

        await Assert.ThrowsAsync<AttemptNotSubmittedError>(() => Handler.HandleForAttemptAsync(_exam.Id, attempt.Id, CancellationToken.None));

        Assert.False(attempt.IsAwaitingEvaluation);
    }

    [Fact]
    public async Task OnePaper_AnInvalidatedResult_CannotBeRechecked()
    {
        var attempt = Submitted(Worded);
        attempt.Invalidate(Guid.NewGuid(), "Cheating.", Fixtures.Now.AddHours(1));

        await Assert.ThrowsAsync<InvalidAttemptError>(() => Handler.HandleForAttemptAsync(_exam.Id, attempt.Id, CancellationToken.None));

        Assert.False(attempt.IsAwaitingEvaluation);
    }

    [Fact]
    public async Task OnePaper_AnAttemptOfAnotherExam_AnswersLikeAMissingOne()
    {
        var attempt = Submitted(Worded);

        await Assert.ThrowsAsync<AttemptNotFoundError>(() => Handler.HandleForAttemptAsync(Guid.NewGuid(), attempt.Id, CancellationToken.None));
    }

    // ---- a whole exam --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ThroughoutAnExam_EveryAttemptWithAnAnswerToSend_IsQueued_EachSavedOnItsOwn()
    {
        var both = Submitted(Worded, "Another wording");
        var one = Submitted(Worded);
        var nothing = Submitted("  paris ");
        Lists(both, one, nothing);

        var result = await Handler.HandleForExamAsync(_exam.Id, CancellationToken.None);

        Assert.Equal(new AiRecheckResultDto(2, 3), result);
        Assert.True(AnswerTo(both, _capital).IsAwaitingEvaluation && AnswerTo(both, _river).IsAwaitingEvaluation);
        Assert.True(AnswerTo(one, _capital).IsAwaitingEvaluation);
        Assert.False(nothing.IsAwaitingEvaluation);
        // One lock, one save and one commit for each attempt that changed; the one with nothing to send is only read.
        await _unitOfWork.Received(3).LockAttemptAsync(Arg.Any<Guid>(), true, Arg.Any<CancellationToken>());
        await _unitOfWork.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _hold.Received(2).CompleteAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ThroughoutAnExam_ASecondPress_FindsNothingLeftToSend()
    {
        var attempt = Submitted(Worded);
        Lists(attempt);
        await Handler.HandleForExamAsync(_exam.Id, CancellationToken.None);
        _unitOfWork.ClearReceivedCalls();

        var again = await Handler.HandleForExamAsync(_exam.Id, CancellationToken.None);

        // The answer is waiting now, so it is not sent again and the check already queued is not thrown away.
        Assert.Equal(new AiRecheckResultDto(0, 0), again);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ThroughoutAnExam_AnAttemptThatIsNoLongerOver_IsPassedOver_NotAnError()
    {
        // Listed as over, but reopened or invalidated between the list and its lock.
        var invalidated = Submitted(Worded);
        invalidated.Invalidate(Guid.NewGuid(), "Cheating.", Fixtures.Now.AddHours(1));
        var queued = Submitted(Worded);
        Lists(invalidated, queued);

        var result = await Handler.HandleForExamAsync(_exam.Id, CancellationToken.None);

        Assert.Equal(new AiRecheckResultDto(1, 1), result);
        Assert.False(invalidated.IsAwaitingEvaluation);
        Assert.True(queued.IsAwaitingEvaluation);
    }

    [Fact]
    public async Task ThroughoutAnExam_WithNoAttempts_NothingIsQueued()
    {
        Lists();

        var result = await Handler.HandleForExamAsync(_exam.Id, CancellationToken.None);

        Assert.Equal(new AiRecheckResultDto(0, 0), result);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ThroughoutAnExam_WithNoEvaluatorSetUp_NothingIsEvenListed()
    {
        _evaluator.IsEnabled.Returns(false);

        await Assert.ThrowsAsync<TextEvaluatorNotSetUpError>(() => Handler.HandleForExamAsync(_exam.Id, CancellationToken.None));

        await _attempts.DidNotReceive().ListOverIdsForExamAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
