using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application;
using ExamPlatform.Modules.ExamRuntime.Application.Commands;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.Invite.Contracts;
using ExamPlatform.Modules.Notifications.Contracts;
using ExamPlatform.Modules.QuestionBank.Contracts;
using NSubstitute;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>Raising, listing and rejecting answer-key disputes (FR-31): who may dispute what, when, and how staff answer.</summary>
public class DisputeHandlerTests
{
    private static readonly DateTime SubmittedAt = Fixtures.Now.AddMinutes(-10);

    private readonly Guid _candidate = Guid.NewGuid();
    private readonly Guid _staff = Guid.NewGuid();
    private readonly FakeClock _clock = new(Fixtures.Now);
    private readonly IExamCatalog _catalog = Substitute.For<IExamCatalog>();
    private readonly IQuestionBank _bank = Substitute.For<IQuestionBank>();
    private readonly IExamRoster _roster = Substitute.For<IExamRoster>();
    private readonly IAttemptRepository _attempts = Substitute.For<IAttemptRepository>();
    private readonly IDisputeRepository _disputes = Substitute.For<IDisputeRepository>();
    private readonly IExamRuntimeUnitOfWork _unitOfWork = Substitute.For<IExamRuntimeUnitOfWork>();
    private readonly IInAppNotifier _inApp = Substitute.For<IInAppNotifier>();
    private readonly QuestionSnapshot _question = Fixtures.Question("2 + 2?");

    private AttemptAccess Access => new(_attempts, _catalog, new AttemptCloser(_bank, _unitOfWork, _clock), _clock);

    private RaiseDisputeHandler Raise(int windowDays = 7) => new(Access, _disputes, _unitOfWork, new DisputePolicy(windowDays), _clock);

    /// <summary>A published exam holding the question, and the candidate's attempt at it, submitted ten minutes ago.</summary>
    private (ExamSnapshot Exam, Attempt Attempt) Submitted(
        ExamResultReleaseMode mode = ExamResultReleaseMode.Instant, DateTime? releaseAt = null, DateTime? submittedAt = null, Guid? owner = null)
    {
        var exam = Fixtures.Exam([_question], resultRelease: mode, resultReleaseTime: releaseAt);
        _catalog.FindAsync(exam.Id, Arg.Any<CancellationToken>()).Returns(exam);
        var submitted = submittedAt ?? SubmittedAt;
        var attempt = Attempt.Start(exam.Id, owner ?? _candidate, 1, submitted.AddMinutes(-20), submitted.AddMinutes(40));
        attempt.RecordAnswer(_question.Id, _question.Wrong(), submitted.AddMinutes(-15));
        attempt.Submit(submitted, 0, 1);
        _attempts.GetByIdAsync(attempt.Id, Arg.Any<CancellationToken>()).Returns(attempt);
        return (exam, attempt);
    }

    // ---- raising ---------------------------------------------------------------------------------------

    [Fact]
    public async Task ACandidate_CanDisputeAQuestionOfAReleasedResult_AndItIsSavedOpen()
    {
        var (exam, attempt) = Submitted();
        Dispute? added = null;
        _disputes.When(d => d.Add(Arg.Any<Dispute>())).Do(call => added = call.Arg<Dispute>());

        var result = await Raise().HandleAsync(attempt.Id, _candidate, _question.Id, "  Both 4 and 22 are right  ", CancellationToken.None);

        Assert.NotNull(added);
        Assert.Equal(DisputeStatus.Open, added.Status);
        Assert.Equal(attempt.Id, added.AttemptId);
        Assert.Equal(exam.Id, added.ExamId);
        Assert.Equal(_candidate, added.CandidateId);
        Assert.Equal(_question.Id, added.QuestionId);
        Assert.Equal(Fixtures.Now, added.RaisedAtUtc);
        Assert.Equal(added.Id, result.Id);
        Assert.Equal("Both 4 and 22 are right", result.Reason);
        Assert.Equal(DisputeStatus.Open, result.Status);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SomeoneElsesAttempt_AnswersLikeAMissingOne_AndSavesNothing()
    {
        var (_, attempt) = Submitted(owner: Guid.NewGuid());

        await Assert.ThrowsAsync<AttemptNotFoundError>(
            () => Raise().HandleAsync(attempt.Id, _candidate, _question.Id, "Wrong key", CancellationToken.None));

        _disputes.DidNotReceiveWithAnyArgs().Add(default!);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Fact]
    public async Task AnAttemptStillInProgress_HasNothingToDispute()
    {
        var exam = Fixtures.Exam([_question]);
        _catalog.FindAsync(exam.Id, Arg.Any<CancellationToken>()).Returns(exam);
        var open = Attempt.Start(exam.Id, _candidate, 1, Fixtures.Now.AddMinutes(-5), Fixtures.Now.AddMinutes(25));
        _attempts.GetByIdAsync(open.Id, Arg.Any<CancellationToken>()).Returns(open);

        await Assert.ThrowsAsync<AttemptNotSubmittedError>(
            () => Raise().HandleAsync(open.Id, _candidate, _question.Id, "Wrong key", CancellationToken.None));
    }

    [Fact]
    public async Task AnInvalidatedResult_HasNothingToDispute()
    {
        var (_, attempt) = Submitted();
        attempt.Invalidate(_staff, "Cheating", Fixtures.Now.AddMinutes(-1));

        await Assert.ThrowsAsync<AttemptInvalidatedError>(
            () => Raise().HandleAsync(attempt.Id, _candidate, _question.Id, "Wrong key", CancellationToken.None));
    }

    [Fact]
    public async Task AKey_CannotBeDisputedBeforeTheCandidateCanSeeIt()
    {
        var (_, attempt) = Submitted(ExamResultReleaseMode.Scheduled, Fixtures.Now.AddDays(1));

        await Assert.ThrowsAsync<ResultsNotReleasedError>(
            () => Raise().HandleAsync(attempt.Id, _candidate, _question.Id, "Wrong key", CancellationToken.None));

        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Fact]
    public async Task WhenDisputesAreSwitchedOff_NoneIsTaken()
    {
        var (_, attempt) = Submitted();

        var error = await Assert.ThrowsAsync<DisputeWindowClosedError>(
            () => Raise(windowDays: 0).HandleAsync(attempt.Id, _candidate, _question.Id, "Wrong key", CancellationToken.None));

        Assert.Equal("dispute_window_closed", error.ErrorCode);
        Assert.Equal(409, error.HttpStatusCode);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Theory]
    [InlineData(-1, true)] // the last instant of the window
    [InlineData(0, false)] // the window has closed
    public async Task TheWindow_ClosesSevenDaysAfterTheResultWasReleased(int secondsFromTheClose, bool allowed)
    {
        var (_, attempt) = Submitted();
        _clock.UtcNow = SubmittedAt.AddDays(7).AddSeconds(secondsFromTheClose);

        var raising = Raise().HandleAsync(attempt.Id, _candidate, _question.Id, "Wrong key", CancellationToken.None);

        if (allowed)
            Assert.Equal(DisputeStatus.Open, (await raising).Status);
        else
            Assert.Contains("ended on", (await Assert.ThrowsAsync<DisputeWindowClosedError>(() => raising)).Message);
    }

    [Fact]
    public async Task AScheduledResult_CanStillBeDisputed_WhenItsReleaseTimeIsRecent_EvenIfTheCandidateSubmittedLongAgo()
    {
        // Submitted ten days ago but only released two days ago: counting from the submission would have closed the window already.
        var (_, attempt) = Submitted(ExamResultReleaseMode.Scheduled, Fixtures.Now.AddDays(-2), submittedAt: Fixtures.Now.AddDays(-10));

        var result = await Raise().HandleAsync(attempt.Id, _candidate, _question.Id, "Wrong key", CancellationToken.None);

        Assert.Equal(DisputeStatus.Open, result.Status);
    }

    [Fact]
    public async Task AQuestionThatWasNotInTheAttempt_CannotBeDisputed()
    {
        var (_, attempt) = Submitted();

        await Assert.ThrowsAsync<QuestionNotInAttemptError>(
            () => Raise().HandleAsync(attempt.Id, _candidate, Guid.NewGuid(), "Wrong key", CancellationToken.None));
    }

    [Fact]
    public async Task ACandidate_DisputesAQuestionOfAnAttemptOnce()
    {
        var (_, attempt) = Submitted();
        _disputes.ExistsAsync(attempt.Id, _question.Id, Arg.Any<CancellationToken>()).Returns(true);

        var error = await Assert.ThrowsAsync<DisputeAlreadyRaisedError>(
            () => Raise().HandleAsync(attempt.Id, _candidate, _question.Id, "Wrong key", CancellationToken.None));

        Assert.Equal("dispute_already_raised", error.ErrorCode);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Fact]
    public async Task ADisputeWithoutAReason_IsRefused_AndSavesNothing()
    {
        var (_, attempt) = Submitted();

        await Assert.ThrowsAsync<InvalidAttemptError>(
            () => Raise().HandleAsync(attempt.Id, _candidate, _question.Id, "   ", CancellationToken.None));

        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    // ---- listing and rejecting -------------------------------------------------------------------------

    private DisputeDtoFactory Dtos => new(_catalog, _roster, _attempts, _bank);

    private Dispute OpenDispute(Guid attemptId, Guid examId, Guid? candidate = null) =>
        Dispute.Raise(attemptId, examId, candidate ?? _candidate, _question.Id, "Both 4 and 22 are right", Fixtures.Now.AddMinutes(-5));

    private void KnowTheExam(ExamSnapshot exam, Attempt attempt)
    {
        _roster.GetEnrolledCandidatesAsync(exam.Id, Arg.Any<CancellationToken>()).Returns([new EnrolledCandidate(_candidate, "asha@example.com")]);
        _attempts.ListForExamAsync(exam.Id, Arg.Any<CancellationToken>()).Returns([attempt]);
        _bank.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([_question]);
    }

    [Fact]
    public async Task TheQueue_ListsOpenDisputesByDefault_WithWhoWhichExamWhichAttemptAndWhichQuestion()
    {
        var (exam, attempt) = Submitted();
        KnowTheExam(exam, attempt);
        var dispute = OpenDispute(attempt.Id, exam.Id);
        _disputes.ListAsync(DisputeStatus.Open, ListDisputesHandler.PageSize, Arg.Any<CancellationToken>()).Returns([dispute]);

        var listed = Assert.Single(await new ListDisputesHandler(_disputes, Dtos).HandleAsync(null, CancellationToken.None));

        Assert.Equal(dispute.Id, listed.Id);
        Assert.Equal(exam.Name, listed.ExamName);
        Assert.Equal(attempt.Id, listed.AttemptId);
        Assert.Equal(1, listed.AttemptNumber);
        Assert.Equal("asha@example.com", listed.CandidateEmail);
        Assert.Equal(_question.Id, listed.QuestionId);
        Assert.Equal("2 + 2?", listed.QuestionText);
        Assert.Equal("Both 4 and 22 are right", listed.Reason);
        Assert.Equal(DisputeStatus.Open, listed.Status);
    }

    [Fact]
    public async Task TheQueue_CanListSettledDisputes()
    {
        _disputes.ListAsync(DisputeStatus.Rejected, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);

        Assert.Empty(await new ListDisputesHandler(_disputes, Dtos).HandleAsync(DisputeStatus.Rejected, CancellationToken.None));

        await _disputes.Received(1).ListAsync(DisputeStatus.Rejected, ListDisputesHandler.PageSize, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ADisputeWhoseExamOrQuestionIsGone_StillLists_WithTheseLeftOut()
    {
        var gone = OpenDispute(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _catalog.FindAsync(gone.ExamId, Arg.Any<CancellationToken>()).Returns((ExamSnapshot?)null);
        _roster.GetEnrolledCandidatesAsync(gone.ExamId, Arg.Any<CancellationToken>()).Returns([]);
        _attempts.ListForExamAsync(gone.ExamId, Arg.Any<CancellationToken>()).Returns([]);
        _bank.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([]);
        _disputes.ListAsync(DisputeStatus.Open, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([gone]);

        var listed = Assert.Single(await new ListDisputesHandler(_disputes, Dtos).HandleAsync(null, CancellationToken.None));

        Assert.Null(listed.ExamName);
        Assert.Null(listed.AttemptNumber);
        Assert.Null(listed.CandidateEmail);
        Assert.Null(listed.QuestionText);
    }

    [Fact]
    public async Task Rejecting_SettlesTheDispute_SavesIt_AndReturnsTheNoteTheCandidateWillSee()
    {
        var (exam, attempt) = Submitted();
        KnowTheExam(exam, attempt);
        var dispute = OpenDispute(attempt.Id, exam.Id);
        _disputes.GetByIdAsync(dispute.Id, Arg.Any<CancellationToken>()).Returns(dispute);

        var result = await new RejectDisputeHandler(_disputes, _unitOfWork, Dtos, _clock, _inApp)
            .HandleAsync(dispute.Id, _staff, "4 is the only sum", CancellationToken.None);

        Assert.Equal(DisputeStatus.Rejected, result.Status);
        Assert.Equal("4 is the only sum", result.ResolutionNote);
        Assert.Equal(Fixtures.Now, result.ResolvedAtUtc);
        Assert.Equal(_staff, dispute.ResolvedByUserId);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _inApp.Received(1).NotifyAsync(
            Arg.Is<InAppNotice>(n => n.RecipientUserId == dispute.CandidateId && n.Kind == InAppNoticeKind.DisputeRejected && n.SubjectId == dispute.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectingAnUnknownDispute_IsNotFound()
    {
        _disputes.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Dispute?)null);

        await Assert.ThrowsAsync<DisputeNotFoundError>(
            () => new RejectDisputeHandler(_disputes, _unitOfWork, Dtos, _clock, _inApp).HandleAsync(Guid.NewGuid(), _staff, "No", CancellationToken.None));
    }

    [Fact]
    public async Task RejectingASettledDispute_IsRefused_AndSavesNothing()
    {
        var dispute = OpenDispute(Guid.NewGuid(), Guid.NewGuid());
        dispute.Accept(_staff, Fixtures.Now, "Corrected");
        _disputes.GetByIdAsync(dispute.Id, Arg.Any<CancellationToken>()).Returns(dispute);

        await Assert.ThrowsAsync<DisputeNotOpenError>(
            () => new RejectDisputeHandler(_disputes, _unitOfWork, Dtos, _clock, _inApp).HandleAsync(dispute.Id, _staff, "No", CancellationToken.None));

        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Fact]
    public async Task RejectingWithoutAnExplanation_IsRefused_AndSavesNothing()
    {
        var dispute = OpenDispute(Guid.NewGuid(), Guid.NewGuid());
        _disputes.GetByIdAsync(dispute.Id, Arg.Any<CancellationToken>()).Returns(dispute);

        await Assert.ThrowsAsync<InvalidAttemptError>(
            () => new RejectDisputeHandler(_disputes, _unitOfWork, Dtos, _clock, _inApp).HandleAsync(dispute.Id, _staff, " ", CancellationToken.None));

        Assert.Equal(DisputeStatus.Open, dispute.Status);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }
}
