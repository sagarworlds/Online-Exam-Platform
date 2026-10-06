using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application;
using ExamPlatform.Modules.ExamRuntime.Application.Commands;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Application.Queries;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Events;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;
using NSubstitute;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>What an administrator can do to a candidate's attempt (FR-29): warn, pause, resume, terminate and invalidate.</summary>
public class AttemptModerationTests
{
    private readonly Guid _candidate = Guid.NewGuid();
    private readonly Guid _admin = Guid.NewGuid();
    private readonly FakeClock _clock = new(Fixtures.Now);
    private readonly IExamCatalog _catalog = Substitute.For<IExamCatalog>();
    private readonly IQuestionBank _bank = Substitute.For<IQuestionBank>();
    private readonly IAttemptRepository _attempts = Substitute.For<IAttemptRepository>();
    private readonly IExamRuntimeUnitOfWork _unitOfWork = Substitute.For<IExamRuntimeUnitOfWork>();
    private readonly QuestionSnapshot _question = Fixtures.Question();
    private readonly ExamSnapshot _exam;

    public AttemptModerationTests()
    {
        _exam = Fixtures.Exam([_question]);
        _catalog.FindAsync(_exam.Id, Arg.Any<CancellationToken>()).Returns(_exam);
        _bank.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<QuestionSnapshot>>([_question]));
    }

    private AttemptCloser Closer => new(_bank, _unitOfWork, _clock);
    private StaffAttemptAccess Access => new(_attempts, _catalog, new AttemptAccess(_attempts, _catalog, Closer, _clock));

    private Attempt OpenAttempt(int minutes = 30)
    {
        var attempt = Attempt.Start(_exam.Id, _candidate, 1, Fixtures.Now, Fixtures.Now.AddMinutes(minutes));
        _attempts.GetByIdAsync(attempt.Id, Arg.Any<CancellationToken>()).Returns(attempt);
        return attempt;
    }

    private static T Raised<T>(Attempt attempt) where T : class => Assert.IsType<T>(attempt.DomainEvents.Single(e => e is T));

    // ---- warn ------------------------------------------------------------------------------------------

    [Fact]
    public void AWarning_IsKept_WithWhoSentItAndWhen_AndRaisesAnEvent()
    {
        var attempt = OpenAttempt();

        attempt.Warn("  Please keep your eyes on your own screen.  ", _admin, Fixtures.Now.AddMinutes(5));

        var warning = Assert.Single(attempt.Warnings);
        Assert.Equal("Please keep your eyes on your own screen.", warning.Message);
        Assert.Equal(_admin, warning.IssuedByUserId);
        Assert.Equal(Fixtures.Now.AddMinutes(5), warning.IssuedAtUtc);
        Assert.Equal("Please keep your eyes on your own screen.", Raised<AttemptWarnedEvent>(attempt).Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyWarning_IsRefused(string? message)
    {
        var attempt = OpenAttempt();

        Assert.Throws<InvalidAttemptError>(() => attempt.Warn(message, _admin, Fixtures.Now));

        Assert.Empty(attempt.Warnings);
    }

    [Fact]
    public void ATooLongWarning_IsRefused()
    {
        var attempt = OpenAttempt();

        Assert.Throws<InvalidAttemptError>(() => attempt.Warn(new string('x', AttemptWarning.MaxMessageLength + 1), _admin, Fixtures.Now));
        attempt.Warn(new string('x', AttemptWarning.MaxMessageLength), _admin, Fixtures.Now);
    }

    [Fact]
    public void AFinishedAttempt_CannotBeWarned()
    {
        var attempt = OpenAttempt();
        attempt.Submit(Fixtures.Now.AddMinutes(1), 0, 1);

        Assert.Throws<AttemptNotInProgressError>(() => attempt.Warn("Too late", _admin, Fixtures.Now));
    }

    [Fact]
    public void APausedAttempt_CanStillBeWarned()
    {
        var attempt = OpenAttempt();
        attempt.Pause(Fixtures.Now.AddMinutes(1));

        attempt.Warn("Stay where you are.", _admin, Fixtures.Now.AddMinutes(2));

        Assert.Single(attempt.Warnings);
    }

    // ---- pause and resume ----------------------------------------------------------------------------------

    [Fact]
    public void APausedAttempt_RefusesEveryChange_ButKeepsItsAnswers()
    {
        var attempt = OpenAttempt();
        attempt.RecordAnswer(_question.Id, _question.Correct(), Fixtures.Now);
        attempt.Pause(Fixtures.Now.AddMinutes(1));

        Assert.Throws<AttemptPausedError>(() => attempt.RecordAnswer(_question.Id, _question.Wrong(), Fixtures.Now.AddMinutes(2)));
        Assert.Throws<AttemptPausedError>(() => attempt.ClearAnswer(_question.Id, Fixtures.Now.AddMinutes(2)));
        Assert.Throws<AttemptPausedError>(() => attempt.SetMarked(_question.Id, true, Fixtures.Now.AddMinutes(2)));
        Assert.Throws<AttemptPausedError>(() => attempt.MoveToSection(1, Fixtures.Now.AddMinutes(2)));
        Assert.Throws<AttemptPausedError>(() => attempt.RecordFocusViolation(FocusViolationKind.TabHidden, Fixtures.Now.AddMinutes(2)));
        Assert.Equal(_question.Correct(), attempt.Answers.Single().SelectedOptionIds.Single());
    }

    [Fact]
    public void ACandidate_CannotSubmitOutOfAPause()
    {
        var attempt = OpenAttempt();
        attempt.Pause(Fixtures.Now.AddMinutes(1));

        Assert.Throws<AttemptPausedError>(() => attempt.Submit(Fixtures.Now.AddMinutes(2), 0, 1));
        Assert.Equal(AttemptStatus.InProgress, attempt.Status);
    }

    [Fact]
    public void APausedAttemptsClockStops_SoItCannotRunOutWhilePaused()
    {
        var attempt = OpenAttempt(minutes: 30);
        attempt.Pause(Fixtures.Now.AddMinutes(10));

        Assert.False(attempt.IsExpired(Fixtures.Now.AddHours(5)));
    }

    [Fact]
    public void Resuming_MovesTheDeadlineLaterByTheTimeSpentPaused_SoNoTimeIsLost()
    {
        var attempt = OpenAttempt(minutes: 30);
        attempt.Pause(Fixtures.Now.AddMinutes(10));

        attempt.Resume(Fixtures.Now.AddMinutes(25));

        Assert.Null(attempt.PausedAtUtc);
        Assert.Equal(Fixtures.Now.AddMinutes(45), attempt.DeadlineUtc);
        Assert.Equal(900, Raised<AttemptResumedEvent>(attempt).PausedSeconds);
        // Twenty minutes were left when it was paused, and twenty are left now.
        Assert.False(attempt.IsExpired(Fixtures.Now.AddMinutes(44)));
        Assert.True(attempt.IsExpired(Fixtures.Now.AddMinutes(45)));
    }

    [Fact]
    public void ACandidate_CanAnswerAgainOnceResumed()
    {
        var attempt = OpenAttempt();
        attempt.Pause(Fixtures.Now.AddMinutes(1));
        attempt.Resume(Fixtures.Now.AddMinutes(2));

        attempt.RecordAnswer(_question.Id, _question.Correct(), Fixtures.Now.AddMinutes(3));

        Assert.Single(attempt.Answers);
    }

    [Fact]
    public void PausingTwice_IsRefused()
    {
        var attempt = OpenAttempt();
        attempt.Pause(Fixtures.Now.AddMinutes(1));

        Assert.Throws<AttemptAlreadyPausedError>(() => attempt.Pause(Fixtures.Now.AddMinutes(2)));
        Assert.Equal(Fixtures.Now.AddMinutes(1), attempt.PausedAtUtc);
    }

    [Fact]
    public void ResumingAnAttemptThatIsNotPaused_IsRefused()
    {
        var attempt = OpenAttempt();

        Assert.Throws<AttemptNotPausedError>(() => attempt.Resume(Fixtures.Now.AddMinutes(1)));
    }

    [Fact]
    public void AnAttemptPastItsDeadline_CannotBePaused()
    {
        var attempt = OpenAttempt(minutes: 30);

        Assert.Throws<AttemptTimeExpiredError>(() => attempt.Pause(Fixtures.Now.AddMinutes(31)));
    }

    [Fact]
    public void AFinishedAttempt_CannotBePausedOrResumed()
    {
        var attempt = OpenAttempt();
        attempt.Submit(Fixtures.Now.AddMinutes(1), 0, 1);

        Assert.Throws<AttemptNotInProgressError>(() => attempt.Pause(Fixtures.Now.AddMinutes(2)));
        Assert.Throws<AttemptNotInProgressError>(() => attempt.Resume(Fixtures.Now.AddMinutes(2)));
    }

    // ---- terminate -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Terminating_EndsTheAttemptScoredWithWhatWasSaved_AndKeepsWhoAndWhy()
    {
        var attempt = OpenAttempt();
        attempt.RecordAnswer(_question.Id, _question.Correct(), Fixtures.Now);
        _clock.UtcNow = Fixtures.Now.AddMinutes(7);

        var summary = await new TerminateAttemptHandler(Access, Closer)
            .HandleAsync(_exam.Id, attempt.Id, _admin, "  Caught with a phone.  ", CancellationToken.None);

        Assert.Equal(AttemptStatus.Submitted, attempt.Status);
        Assert.Equal(1m, attempt.Score);
        Assert.True(attempt.AutoSubmitted);
        Assert.False(attempt.EndedByViolations);
        Assert.Equal(_admin, attempt.TerminatedByUserId);
        Assert.Equal("Caught with a phone.", attempt.TerminationReason);
        Assert.Equal(Fixtures.Now.AddMinutes(7), attempt.SubmittedAtUtc);
        Assert.True(summary.TerminatedByAdmin);
        Assert.Equal("Caught with a phone.", Raised<AttemptTerminatedEvent>(attempt).Reason);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task APausedAttempt_CanBeTerminated()
    {
        var attempt = OpenAttempt();
        attempt.Pause(Fixtures.Now.AddMinutes(1));

        await new TerminateAttemptHandler(Access, Closer).HandleAsync(_exam.Id, attempt.Id, _admin, "Cheating", CancellationToken.None);

        Assert.Equal(AttemptStatus.Submitted, attempt.Status);
        Assert.Null(attempt.PausedAtUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task Terminating_NeedsAReason_AndChangesNothingWithout(string? reason)
    {
        var attempt = OpenAttempt();

        await Assert.ThrowsAsync<InvalidAttemptError>(
            () => new TerminateAttemptHandler(Access, Closer).HandleAsync(_exam.Id, attempt.Id, _admin, reason, CancellationToken.None));

        Assert.Equal(AttemptStatus.InProgress, attempt.Status);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AFinishedAttempt_CannotBeTerminated()
    {
        var attempt = OpenAttempt();
        attempt.Submit(Fixtures.Now.AddMinutes(1), 0, 1);

        await Assert.ThrowsAsync<AttemptNotInProgressError>(
            () => new TerminateAttemptHandler(Access, Closer).HandleAsync(_exam.Id, attempt.Id, _admin, "Too late", CancellationToken.None));
    }

    // ---- invalidate ----------------------------------------------------------------------------------------

    [Fact]
    public void Invalidating_KeepsTheScoreForTheRecord_AndSaysWhoAndWhy()
    {
        var attempt = OpenAttempt();
        attempt.Submit(Fixtures.Now.AddMinutes(5), 1, 1);

        attempt.Invalidate(_admin, "Shared answers with another candidate.", Fixtures.Now.AddDays(1));

        Assert.True(attempt.IsInvalidated);
        Assert.Equal(1m, attempt.Score);
        Assert.Equal(_admin, attempt.InvalidatedByUserId);
        Assert.Equal(Fixtures.Now.AddDays(1), attempt.InvalidatedAtUtc);
        Assert.Equal("Shared answers with another candidate.", Raised<AttemptInvalidatedEvent>(attempt).Reason);
    }

    [Fact]
    public void AnOpenAttempt_CannotBeInvalidated_BecauseItHasNoResultYet()
    {
        var attempt = OpenAttempt();

        Assert.Throws<AttemptNotSubmittedError>(() => attempt.Invalidate(_admin, "Cheating", Fixtures.Now));
    }

    [Fact]
    public void InvalidatingTwice_IsRefused_AndKeepsTheFirstReason()
    {
        var attempt = OpenAttempt();
        attempt.Submit(Fixtures.Now.AddMinutes(5), 1, 1);
        attempt.Invalidate(_admin, "First", Fixtures.Now);

        Assert.Throws<AttemptAlreadyInvalidatedError>(() => attempt.Invalidate(_admin, "Second", Fixtures.Now));
        Assert.Equal("First", attempt.InvalidationReason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Invalidating_NeedsAReason(string? reason)
    {
        var attempt = OpenAttempt();
        attempt.Submit(Fixtures.Now.AddMinutes(5), 1, 1);

        Assert.Throws<InvalidAttemptError>(() => attempt.Invalidate(_admin, reason, Fixtures.Now));
        Assert.False(attempt.IsInvalidated);
    }

    // ---- the handlers' view of it ---------------------------------------------------------------------------

    [Fact]
    public async Task AnAttemptOfAnotherExam_IsNotFound_AndNothingChanges()
    {
        var attempt = OpenAttempt();

        await Assert.ThrowsAsync<AttemptNotFoundError>(
            () => new PauseAttemptHandler(Access, _unitOfWork, _clock).HandleAsync(Guid.NewGuid(), attempt.Id, CancellationToken.None));

        Assert.Null(attempt.PausedAtUtc);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnUnknownAttempt_IsNotFound()
    {
        await Assert.ThrowsAsync<AttemptNotFoundError>(
            () => new WarnAttemptHandler(Access, _unitOfWork, _clock).HandleAsync(_exam.Id, Guid.NewGuid(), _admin, "Hi", CancellationToken.None));
    }

    [Fact]
    public async Task TheStaffSummary_ShowsPausedAndTheCounts_AndKeepsTheScoreOfAnInvalidatedResult()
    {
        var attempt = OpenAttempt();
        attempt.RecordFocusViolation(FocusViolationKind.TabHidden, Fixtures.Now);
        attempt.Warn("Careful", _admin, Fixtures.Now);

        var paused = await new PauseAttemptHandler(Access, _unitOfWork, _clock).HandleAsync(_exam.Id, attempt.Id, CancellationToken.None);

        Assert.True(paused.Paused);
        Assert.Equal(1, paused.FocusViolations);
        Assert.Equal(1, paused.Warnings);

        attempt.Resume(Fixtures.Now.AddMinutes(1));
        attempt.Submit(Fixtures.Now.AddMinutes(2), 1, 1);
        attempt.Invalidate(_admin, "Cheating", Fixtures.Now.AddMinutes(3));

        var staff = ExamCandidateRows.StaffSummary(attempt);
        Assert.True(staff.Invalidated);
        Assert.Equal(1m, staff.Score);
    }

    [Fact]
    public void TheCandidatesOwnSummary_HidesTheScoreOfAnInvalidatedResult_AndSaysWhy()
    {
        var attempt = OpenAttempt();
        attempt.Submit(Fixtures.Now.AddMinutes(2), 1, 1);
        attempt.Invalidate(_admin, "Cheating", Fixtures.Now.AddMinutes(3));

        var summary = ExamCandidateRows.Summary(attempt);

        Assert.True(summary.Invalidated);
        Assert.Equal("Cheating", summary.InvalidationReason);
        Assert.Null(summary.Score);
        Assert.Null(summary.MaxScore);
    }

    [Fact]
    public async Task TheCandidatesView_ShowsAnInvalidatedResultWithoutAScoreOrReview()
    {
        var attempt = OpenAttempt();
        attempt.Submit(Fixtures.Now.AddMinutes(2), 1, 1);
        attempt.Invalidate(_admin, "Cheating", Fixtures.Now.AddMinutes(3));

        var dto = await new AttemptViewBuilder(_bank, _clock).BuildAsync(attempt, _exam, CancellationToken.None);

        Assert.True(dto.Invalidated);
        Assert.Equal("Cheating", dto.InvalidationReason);
        Assert.Null(dto.Score);
        Assert.Null(dto.MaxScore);
        Assert.Null(dto.Review);
    }

    [Fact]
    public async Task AnInvalidatedResult_HasNoAnswerReview()
    {
        var attempt = OpenAttempt();
        attempt.Submit(Fixtures.Now.AddMinutes(2), 1, 1);
        attempt.Invalidate(_admin, "Cheating", Fixtures.Now.AddMinutes(3));
        var access = new AttemptAccess(_attempts, _catalog, Closer, _clock);

        await Assert.ThrowsAsync<AttemptInvalidatedError>(
            () => new GetAttemptReviewHandler(access, new AttemptReviewBuilder(_bank, _clock)).HandleAsync(attempt.Id, _candidate, CancellationToken.None));
    }

    [Fact]
    public async Task TheHeartbeat_ReportsPauseDeadlineAndWarnings_WithoutTheQuestions()
    {
        var attempt = OpenAttempt();
        attempt.Warn("First", _admin, Fixtures.Now.AddMinutes(1));
        attempt.Pause(Fixtures.Now.AddMinutes(2));
        var access = new AttemptAccess(_attempts, _catalog, Closer, _clock);

        var status = await new GetAttemptStatusHandler(access, _clock, new FakeClientInfo(), _unitOfWork).HandleAsync(attempt.Id, _candidate, CancellationToken.None);

        Assert.Equal(AttemptStatus.InProgress, status.Status);
        Assert.Equal(Fixtures.Now.AddMinutes(2), status.PausedAtUtc);
        Assert.Equal(attempt.DeadlineUtc, status.DeadlineUtc);
        Assert.Equal(Fixtures.Now, status.ServerTimeUtc);
        Assert.Equal(["First"], status.Warnings.Select(w => w.Message));
    }

    [Fact]
    public async Task TheHeartbeat_OfSomeoneElsesAttempt_IsNotFound()
    {
        var attempt = OpenAttempt();
        var access = new AttemptAccess(_attempts, _catalog, Closer, _clock);

        await Assert.ThrowsAsync<AttemptNotFoundError>(
            () => new GetAttemptStatusHandler(access, _clock, new FakeClientInfo(), _unitOfWork).HandleAsync(attempt.Id, Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task APausedAttempt_IsNotClosedWhenItsOriginalDeadlinePasses()
    {
        var attempt = OpenAttempt(minutes: 30);
        attempt.Pause(Fixtures.Now.AddMinutes(10));
        _clock.UtcNow = Fixtures.Now.AddHours(3);
        var access = new AttemptAccess(_attempts, _catalog, Closer, _clock);

        var (loaded, _) = await access.LoadOwnedAsync(attempt.Id, _candidate, CancellationToken.None);

        Assert.Equal(AttemptStatus.InProgress, loaded.Status);
    }
}
