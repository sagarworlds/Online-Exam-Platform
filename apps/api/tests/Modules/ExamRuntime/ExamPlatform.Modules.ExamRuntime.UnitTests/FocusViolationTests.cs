using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application;
using ExamPlatform.Modules.ExamRuntime.Application.Commands;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;
using NSubstitute;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>A candidate leaving the exam page (FR-22): what an attempt keeps, and when the server ends it.</summary>
public class FocusViolationTests
{
    private readonly Guid _candidate = Guid.NewGuid();
    private readonly FakeClock _clock = new(Fixtures.Now);
    private readonly IExamCatalog _catalog = Substitute.For<IExamCatalog>();
    private readonly IQuestionBank _bank = Substitute.For<IQuestionBank>();
    private readonly IAttemptRepository _attempts = Substitute.For<IAttemptRepository>();
    private readonly IExamRuntimeUnitOfWork _unitOfWork = Substitute.For<IExamRuntimeUnitOfWork>();
    private readonly QuestionSnapshot _question = Fixtures.Question();
    private ExamSnapshot _exam;

    public FocusViolationTests()
    {
        _exam = Fixtures.Exam([_question]) with { FocusViolationLimit = 3 };
        _catalog.FindAsync(_exam.Id, Arg.Any<CancellationToken>()).Returns(_ => _exam);
        _bank.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<QuestionSnapshot>>([_question]));
    }

    private RecordFocusViolationHandler Handler()
    {
        var closer = new AttemptCloser(_bank, _unitOfWork, _clock);
        return new RecordFocusViolationHandler(new AttemptAccess(_attempts, _catalog, closer, _clock), closer, _unitOfWork, _clock);
    }

    private Attempt OpenAttempt()
    {
        var attempt = Attempt.Start(_exam.Id, _candidate, 1, Fixtures.Now, Fixtures.Now.AddMinutes(30));
        _attempts.GetByIdAsync(attempt.Id, Arg.Any<CancellationToken>()).Returns(attempt);
        return attempt;
    }

    // ---- the attempt ------------------------------------------------------------------------------

    [Fact]
    public void TheAttempt_KeepsEachDeparture_WithHowAndWhen_AndCountsThem()
    {
        var attempt = OpenAttempt();

        Assert.Equal(1, attempt.RecordFocusViolation(FocusViolationKind.TabHidden, Fixtures.Now.AddMinutes(1)));
        Assert.Equal(2, attempt.RecordFocusViolation(FocusViolationKind.FullscreenExited, Fixtures.Now.AddMinutes(2)));

        Assert.Equal([FocusViolationKind.TabHidden, FocusViolationKind.FullscreenExited], attempt.FocusViolations.Select(v => v.Kind));
        Assert.Equal(Fixtures.Now.AddMinutes(2), attempt.FocusViolations[1].OccurredAtUtc);
    }

    [Fact]
    public void ASubmittedAttempt_RefusesMore()
    {
        var attempt = OpenAttempt();
        attempt.Submit(Fixtures.Now.AddMinutes(1), 0, 1);

        Assert.Throws<AttemptNotInProgressError>(() => attempt.RecordFocusViolation(FocusViolationKind.TabHidden, Fixtures.Now.AddMinutes(2)));
    }

    [Fact]
    public void AnAttemptPastItsDeadline_RefusesMore()
    {
        var attempt = OpenAttempt();

        Assert.Throws<AttemptTimeExpiredError>(() => attempt.RecordFocusViolation(FocusViolationKind.TabHidden, Fixtures.Now.AddMinutes(31)));
    }

    [Fact]
    public void AnAttemptEndedByViolations_IsMarkedAsEndedByTheServer_NotByTheCandidate_AtTheMomentItHappened()
    {
        var attempt = OpenAttempt();

        attempt.Submit(Fixtures.Now.AddMinutes(5), 1, 1, endedByViolations: true);

        Assert.True(attempt.EndedByViolations);
        Assert.True(attempt.AutoSubmitted);
        Assert.Equal(Fixtures.Now.AddMinutes(5), attempt.SubmittedAtUtc);
    }

    [Fact]
    public void AnOrdinarySubmit_IsNeitherAutoSubmittedNorEndedByViolations()
    {
        var attempt = OpenAttempt();

        attempt.Submit(Fixtures.Now.AddMinutes(5), 1, 1);

        Assert.False(attempt.EndedByViolations);
        Assert.False(attempt.AutoSubmitted);
    }

    // ---- the handler ------------------------------------------------------------------------------

    [Fact]
    public async Task ADepartureBelowTheLimit_IsRecordedAndSaved_AndTheAttemptStaysOpen()
    {
        var attempt = OpenAttempt();

        var result = await Handler().HandleAsync(attempt.Id, _candidate, FocusViolationKind.TabHidden, CancellationToken.None);

        Assert.Equal(new(1, 3, false), result);
        Assert.Equal(AttemptStatus.InProgress, attempt.Status);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheDepartureThatReachesTheLimit_EndsTheAttempt_ScoresWhatWasSaved_AndKeepsTheRecord()
    {
        var attempt = OpenAttempt();
        attempt.RecordAnswer(_question.Id, _question.Correct(), Fixtures.Now);
        attempt.RecordFocusViolation(FocusViolationKind.TabHidden, Fixtures.Now);
        attempt.RecordFocusViolation(FocusViolationKind.WindowBlurred, Fixtures.Now);

        var result = await Handler().HandleAsync(attempt.Id, _candidate, FocusViolationKind.FullscreenExited, CancellationToken.None);

        Assert.Equal(new(3, 3, true), result);
        Assert.Equal(AttemptStatus.Submitted, attempt.Status);
        Assert.True(attempt.EndedByViolations);
        Assert.Equal(1m, attempt.Score);
        Assert.Equal(3, attempt.FocusViolations.Count);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnExamThatDoesNotWatch_RecordsNothing_AndSavesNothing()
    {
        _exam = _exam with { FocusViolationLimit = 0 };
        var attempt = OpenAttempt();

        var result = await Handler().HandleAsync(attempt.Id, _candidate, FocusViolationKind.TabHidden, CancellationToken.None);

        Assert.Equal(new(0, 0, false), result);
        Assert.Empty(attempt.FocusViolations);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnExamThatDoesNotWatch_StillTellsThePageTheSittingIsOver()
    {
        _exam = _exam with { FocusViolationLimit = 0 };
        var attempt = OpenAttempt();
        attempt.Submit(Fixtures.Now.AddMinutes(1), 0, 1);

        await Assert.ThrowsAsync<AttemptNotInProgressError>(
            () => Handler().HandleAsync(attempt.Id, _candidate, FocusViolationKind.TabHidden, CancellationToken.None));
    }

    [Fact]
    public async Task ASubmittedAttempt_RefusesTheReport()
    {
        var attempt = OpenAttempt();
        attempt.Submit(Fixtures.Now.AddMinutes(1), 0, 1);

        await Assert.ThrowsAsync<AttemptNotInProgressError>(
            () => Handler().HandleAsync(attempt.Id, _candidate, FocusViolationKind.TabHidden, CancellationToken.None));
    }

    [Fact]
    public async Task SomeoneElsesAttempt_IsNotFound_AndNothingIsRecorded()
    {
        var attempt = OpenAttempt();

        await Assert.ThrowsAsync<AttemptNotFoundError>(
            () => Handler().HandleAsync(attempt.Id, Guid.NewGuid(), FocusViolationKind.TabHidden, CancellationToken.None));

        Assert.Empty(attempt.FocusViolations);
    }

    [Fact]
    public async Task TheCandidatesView_ShowsTheLimitAndTheCountSoFar_SoAReloadDoesNotResetIt()
    {
        var attempt = OpenAttempt();
        attempt.RecordFocusViolation(FocusViolationKind.TabHidden, Fixtures.Now);

        var dto = await new AttemptViewBuilder(_bank, _clock).BuildAsync(attempt, _exam, CancellationToken.None);

        Assert.Equal(3, dto.FocusViolationLimit);
        Assert.Equal(1, dto.FocusViolations);
        Assert.False(dto.EndedByViolations);
    }
}
