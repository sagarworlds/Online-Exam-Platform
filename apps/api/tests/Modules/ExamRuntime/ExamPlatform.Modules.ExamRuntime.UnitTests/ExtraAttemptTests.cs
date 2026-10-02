using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application;
using ExamPlatform.Modules.ExamRuntime.Application.Commands;
using ExamPlatform.Modules.ExamRuntime.Application.Dtos;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Application.Queries;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.Invite.Contracts;
using ExamPlatform.Modules.QuestionBank.Contracts;
using NSubstitute;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>The allowance rule and the grant entity, which carry no dependencies.</summary>
public class AttemptAllowanceTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(3, 4)]
    public void EveryoneHasOneAttempt_AndEachGrantAddsOne(int grants, int allowed) => Assert.Equal(allowed, AttemptAllowance.Allowed(grants));

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(1, 0, false)]
    [InlineData(1, 1, true)]
    [InlineData(2, 1, false)]
    public void AnotherAttemptMayStart_OnlyWhileOneIsLeft(int made, int grants, bool expected)
    {
        Assert.Equal(expected, AttemptAllowance.CanStartAnother(made, grants));
    }

    [Theory]
    [InlineData(0, 0, false)] // has not even used the first one
    [InlineData(1, 0, true)]
    [InlineData(1, 1, false)] // a grant not yet used: a double click must not stockpile another
    [InlineData(2, 1, true)]
    public void AnotherAttemptMayBeGranted_OnlyOnceEveryHeldAttemptIsUsed(int made, int grants, bool expected)
    {
        Assert.Equal(expected, AttemptAllowance.CanGrant(made, grants));
    }

    [Fact]
    public void AGrantRecordsWhoAllowedItWhenAndWhy_TrimmingTheReason()
    {
        var by = Guid.NewGuid();

        var grant = ExtraAttemptGrant.Create(Guid.NewGuid(), Guid.NewGuid(), 1, by, Fixtures.Now, "  Power cut during the exam  ");

        Assert.Equal(by, grant.GrantedByUserId);
        Assert.Equal(Fixtures.Now, grant.GrantedAtUtc);
        Assert.Equal(1, grant.Number);
        Assert.Equal("Power cut during the exam", grant.Reason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankReason_IsStoredAsNone(string? reason)
    {
        Assert.Null(ExtraAttemptGrant.Create(Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Fixtures.Now, reason).Reason);
    }

    [Fact]
    public void AReasonThatIsTooLong_IsRefused()
    {
        var tooLong = new string('x', ExtraAttemptGrant.MaxReasonLength + 1);

        Assert.Throws<InvalidAttemptError>(() => ExtraAttemptGrant.Create(Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Fixtures.Now, tooLong));
        // Exactly the limit is fine.
        ExtraAttemptGrant.Create(Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Fixtures.Now, new string('x', ExtraAttemptGrant.MaxReasonLength));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AnAttemptIsNumberedFromOne(int number)
    {
        Assert.Throws<InvalidAttemptError>(() => Attempt.Start(Guid.NewGuid(), Guid.NewGuid(), number, Fixtures.Now, Fixtures.Now.AddHours(1)));
        Assert.Equal(2, Attempt.Start(Guid.NewGuid(), Guid.NewGuid(), 2, Fixtures.Now, Fixtures.Now.AddHours(1)).Number);
    }
}

/// <summary>
/// A candidate who has used their attempt, and an administrator who gives them another (the flow as the handlers see it),
/// with the other modules replaced by fakes.
/// </summary>
public class ExtraAttemptHandlerTests
{
    private readonly Guid _candidate = Guid.NewGuid();
    private readonly Guid _admin = Guid.NewGuid();
    private readonly FakeClock _clock = new(Fixtures.Now);
    private readonly IExamCatalog _catalog = Substitute.For<IExamCatalog>();
    private readonly IEnrollments _enrollments = Substitute.For<IEnrollments>();
    private readonly IExamRoster _roster = Substitute.For<IExamRoster>();
    private readonly IQuestionBank _bank = Substitute.For<IQuestionBank>();
    private readonly IAttemptRepository _attempts = Substitute.For<IAttemptRepository>();
    private readonly IExtraAttemptGrantRepository _grants = Substitute.For<IExtraAttemptGrantRepository>();
    private readonly IExamRuntimeUnitOfWork _unitOfWork = Substitute.For<IExamRuntimeUnitOfWork>();

    private readonly QuestionSnapshot _q1 = Fixtures.Question("First");
    private readonly QuestionSnapshot _q2 = Fixtures.Question("Second");
    private readonly ExamSnapshot _exam;
    private readonly List<Attempt> _theirs = [];
    private int _granted;

    public ExtraAttemptHandlerTests()
    {
        _exam = Fixtures.Exam([_q1, _q2]);
        _catalog.FindAsync(_exam.Id, Arg.Any<CancellationToken>()).Returns(_exam);
        _enrollments.IsEnrolledAsync(_candidate, _exam.Id, Arg.Any<CancellationToken>()).Returns(true);
        _roster.GetEnrolledCandidatesAsync(_exam.Id, Arg.Any<CancellationToken>())
            .Returns([new EnrolledCandidate(_candidate, "student@example.com")]);
        _attempts.ListForCandidateAtExamAsync(_exam.Id, _candidate, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyList<Attempt>>(_theirs.OrderBy(a => a.Number).ToList()));
        _grants.CountAsync(_exam.Id, _candidate, Arg.Any<CancellationToken>()).Returns(_ => _granted);
        _grants.When(g => g.Add(Arg.Any<ExtraAttemptGrant>())).Do(_ => _granted++);
        _bank.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<QuestionSnapshot>>(
                new[] { _q1, _q2 }.Where(q => call.Arg<IReadOnlyCollection<Guid>>().Contains(q.Id)).ToList()));
    }

    private AttemptViewBuilder Views => new(_bank, _clock);
    private AttemptAccess Access => new(_attempts, _catalog, new AttemptCloser(_bank, _unitOfWork, _clock), _clock);
    private StartAttemptHandler Start => new(_catalog, _enrollments, _attempts, _grants, _unitOfWork, Access, Views, _clock);
    private GrantExtraAttemptHandler Grant => new(_catalog, _roster, _attempts, _grants, _unitOfWork, _clock);

    /// <summary>An attempt the candidate has already made: submitted unless <paramref name="open"/>.</summary>
    private Attempt Made(bool open = false, DateTime? startedAt = null, DateTime? deadline = null, decimal score = 1m)
    {
        var started = startedAt ?? Fixtures.Now.AddMinutes(-20);
        var attempt = Attempt.Start(_exam.Id, _candidate, _theirs.Count + 1, started, deadline ?? started.AddMinutes(30));
        if (!open)
            attempt.Submit(started.AddMinutes(10), score, 2m);
        _theirs.Add(attempt);
        _attempts.GetByIdAsync(attempt.Id, Arg.Any<CancellationToken>()).Returns(attempt);
        return attempt;
    }

    // ---- starting a further attempt --------------------------------------------------------------------------------

    [Fact]
    public async Task Start_AfterTheOnlyAttemptIsSubmitted_ReturnsItsResult_AndCreatesNothing()
    {
        var first = Made();

        var dto = await Start.HandleAsync(_exam.Id, _candidate, CancellationToken.None);

        Assert.Equal(first.Id, dto.Id);
        Assert.Equal(AttemptStatus.Submitted, dto.Status);
        _attempts.DidNotReceive().Add(Arg.Any<Attempt>());
    }

    [Fact]
    public async Task Start_WithAnExtraAttemptGranted_BeginsAttemptTwo_WithAFreshDeadline()
    {
        Made();
        _granted = 1;

        var dto = await Start.HandleAsync(_exam.Id, _candidate, CancellationToken.None);

        Assert.Equal(AttemptStatus.InProgress, dto.Status);
        Assert.Equal(2, dto.Number);
        Assert.Equal(Fixtures.Now.AddMinutes(30), dto.DeadlineUtc);
        Assert.Equal(2, dto.Sections.Single().Questions.Count);
        _attempts.Received(1).Add(Arg.Is<Attempt>(a => a.Number == 2 && a.CandidateId == _candidate));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Start_WhileTheExtraAttemptIsOpen_ResumesIt_AndCreatesNoThird()
    {
        Made();
        var second = Made(open: true, startedAt: Fixtures.Now.AddMinutes(-5));
        _granted = 1;

        var dto = await Start.HandleAsync(_exam.Id, _candidate, CancellationToken.None);

        Assert.Equal(second.Id, dto.Id);
        Assert.Equal(AttemptStatus.InProgress, dto.Status);
        _attempts.DidNotReceive().Add(Arg.Any<Attempt>());
    }

    [Fact]
    public async Task Start_AfterTheExtraAttemptIsUsedToo_ReturnsTheLatest_AndNeedsAnotherGrantForAThird()
    {
        Made();
        var second = Made();
        _granted = 1;

        var dto = await Start.HandleAsync(_exam.Id, _candidate, CancellationToken.None);

        Assert.Equal(second.Id, dto.Id);
        Assert.Equal(2, dto.Number);
        _attempts.DidNotReceive().Add(Arg.Any<Attempt>());

        _granted = 2;
        var third = await Start.HandleAsync(_exam.Id, _candidate, CancellationToken.None);
        Assert.Equal(3, third.Number);
    }

    [Fact]
    public async Task Start_WhenTheOpenAttemptRanOutOfTime_ClosesIt_AndDoesNotBeginTheNextInTheSameCall()
    {
        var first = Made(open: true, startedAt: Fixtures.Now.AddMinutes(-40), deadline: Fixtures.Now.AddMinutes(-10));
        _granted = 1; // granted while the first was still open

        var dto = await Start.HandleAsync(_exam.Id, _candidate, CancellationToken.None);

        Assert.Equal(first.Id, dto.Id);
        Assert.Equal(AttemptStatus.Submitted, dto.Status);
        Assert.True(dto.AutoSubmitted);
        _attempts.DidNotReceive().Add(Arg.Any<Attempt>());
    }

    [Fact]
    public async Task Start_OfAnExtraAttempt_AfterTheWindowClosed_IsRefused()
    {
        Made(startedAt: Fixtures.Now.AddMinutes(-60));
        _granted = 1;
        _clock.UtcNow = _exam.EndUtc.AddMinutes(1);

        await Assert.ThrowsAsync<ExamClosedError>(() => Start.HandleAsync(_exam.Id, _candidate, CancellationToken.None));

        _attempts.DidNotReceive().Add(Arg.Any<Attempt>());
    }

    [Fact]
    public async Task Start_OfAnExtraAttempt_AfterTheLateEntryCutoff_IsRefused()
    {
        var exam = Fixtures.Exam([_q1, _q2], lateEntry: Fixtures.Now.AddMinutes(-30));
        _catalog.FindAsync(exam.Id, Arg.Any<CancellationToken>()).Returns(exam);
        _enrollments.IsEnrolledAsync(_candidate, exam.Id, Arg.Any<CancellationToken>()).Returns(true);
        var first = Attempt.Start(exam.Id, _candidate, 1, Fixtures.Now.AddMinutes(-50), Fixtures.Now.AddMinutes(-20));
        first.Submit(Fixtures.Now.AddMinutes(-40), 1m, 2m);
        _attempts.ListForCandidateAtExamAsync(exam.Id, _candidate, Arg.Any<CancellationToken>()).Returns([first]);
        _grants.CountAsync(exam.Id, _candidate, Arg.Any<CancellationToken>()).Returns(1);

        await Assert.ThrowsAsync<ExamClosedError>(() => Start.HandleAsync(exam.Id, _candidate, CancellationToken.None));
    }

    // ---- granting --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Grant_ToACandidateWhoUsedTheirAttempt_RecordsWhoAllowedItAndWhy_AndReportsTheNewAllowance()
    {
        Made();

        var row = await Grant.HandleAsync(_exam.Id, _candidate, _admin, "  Lost connection  ", CancellationToken.None);

        _grants.Received(1).Add(Arg.Is<ExtraAttemptGrant>(g =>
            g.ExamId == _exam.Id && g.CandidateId == _candidate && g.Number == 1 && g.GrantedByUserId == _admin && g.Reason == "Lost connection" && g.GrantedAtUtc == Fixtures.Now));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        Assert.Equal("student@example.com", row.Email);
        Assert.Equal(2, row.AttemptsAllowed);
        Assert.Equal(1, row.AttemptsUsed);
        Assert.False(row.CanGrant); // the new one has not been used yet
        Assert.Equal(1, Assert.Single(row.Attempts).Number);
    }

    [Fact]
    public async Task Grant_Twice_InARow_IsRefusedTheSecondTime_SoADoubleClickCannotStockpile()
    {
        Made();
        await Grant.HandleAsync(_exam.Id, _candidate, _admin, null, CancellationToken.None);

        await Assert.ThrowsAsync<AttemptAvailableError>(() => Grant.HandleAsync(_exam.Id, _candidate, _admin, null, CancellationToken.None));

        _grants.Received(1).Add(Arg.Any<ExtraAttemptGrant>());
    }

    [Fact]
    public async Task Grant_ToACandidateWhoHasNotUsedTheirFirstAttempt_IsRefused()
    {
        var error = await Assert.ThrowsAsync<AttemptAvailableError>(() => Grant.HandleAsync(_exam.Id, _candidate, _admin, null, CancellationToken.None));

        Assert.Equal("attempt_available", error.ErrorCode);
        Assert.Equal(409, error.HttpStatusCode);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Grant_ToSomeoneNotEnrolled_IsNotFound()
    {
        var error = await Assert.ThrowsAsync<CandidateNotEnrolledError>(() => Grant.HandleAsync(_exam.Id, Guid.NewGuid(), _admin, null, CancellationToken.None));

        Assert.Equal(404, error.HttpStatusCode);
        _grants.DidNotReceive().Add(Arg.Any<ExtraAttemptGrant>());
    }

    [Fact]
    public async Task Grant_ForAnUnknownExam_IsNotFound()
    {
        var error = await Assert.ThrowsAsync<ExamNotFoundError>(() => Grant.HandleAsync(Guid.NewGuid(), _candidate, _admin, null, CancellationToken.None));

        Assert.Equal("exam_not_found", error.ErrorCode);
    }

    [Fact]
    public async Task Grant_AfterNobodyCanStartTheExamAnyMore_IsRefused_BecauseItCouldNeverBeUsed()
    {
        Made(startedAt: Fixtures.Now.AddMinutes(-60));
        _clock.UtcNow = _exam.EndUtc.AddSeconds(1);

        await Assert.ThrowsAsync<ExamClosedError>(() => Grant.HandleAsync(_exam.Id, _candidate, _admin, null, CancellationToken.None));

        _grants.DidNotReceive().Add(Arg.Any<ExtraAttemptGrant>());
    }

    [Fact]
    public async Task Grant_WithAReasonThatIsTooLong_SavesNothing()
    {
        Made();

        await Assert.ThrowsAsync<InvalidAttemptError>(() => Grant.HandleAsync(_exam.Id, _candidate, _admin, new string('x', 501), CancellationToken.None));

        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Grant_AgainOnceTheExtraAttemptIsUsed_IsTheSecondGrant()
    {
        Made();
        await Grant.HandleAsync(_exam.Id, _candidate, _admin, null, CancellationToken.None);
        Made(); // the extra attempt, used

        await Grant.HandleAsync(_exam.Id, _candidate, _admin, null, CancellationToken.None);

        _grants.Received(1).Add(Arg.Is<ExtraAttemptGrant>(g => g.Number == 2));
    }

    // ---- what staff see ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheStaffList_ShowsEachEnrolledCandidate_WithTheirAttemptsAndWhetherAnotherCanBeGranted()
    {
        var other = Guid.NewGuid();
        _roster.GetEnrolledCandidatesAsync(_exam.Id, Arg.Any<CancellationToken>())
            .Returns([new EnrolledCandidate(_candidate, "student@example.com"), new EnrolledCandidate(other, "other@example.com")]);
        var theirFirst = Made(score: 2m);
        _attempts.ListForExamAsync(_exam.Id, Arg.Any<CancellationToken>()).Returns(_theirs);
        _grants.CountsForExamAsync(_exam.Id, Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, int>());
        var handler = new ListExamAttemptsHandler(_catalog, _roster, _attempts, _grants, _clock);

        var list = await handler.HandleAsync(_exam.Id, CancellationToken.None);

        Assert.False(list.WindowClosed);
        var first = list.Candidates.Single(c => c.CandidateId == _candidate);
        Assert.Equal((1, 1, true), (first.AttemptsAllowed, first.AttemptsUsed, first.CanGrant));
        Assert.Equal(theirFirst.Id, Assert.Single(first.Attempts).Id);
        Assert.Equal(2m, first.Attempts[0].Score);
        var untouched = list.Candidates.Single(c => c.CandidateId == other);
        Assert.Equal((1, 0, false), (untouched.AttemptsAllowed, untouched.AttemptsUsed, untouched.CanGrant));
        Assert.Empty(untouched.Attempts);
    }

    [Fact]
    public async Task TheStaffList_CountsGrants_AndTurnsGrantingOffOnceTheWindowHasClosed()
    {
        Made();
        Made();
        _attempts.ListForExamAsync(_exam.Id, Arg.Any<CancellationToken>()).Returns(_theirs);
        _grants.CountsForExamAsync(_exam.Id, Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, int> { [_candidate] = 1 });
        var handler = new ListExamAttemptsHandler(_catalog, _roster, _attempts, _grants, _clock);

        var open = await handler.HandleAsync(_exam.Id, CancellationToken.None);
        _clock.UtcNow = _exam.EndUtc.AddMinutes(1);
        var closed = await handler.HandleAsync(_exam.Id, CancellationToken.None);

        var row = Assert.Single(open.Candidates);
        Assert.Equal((2, 2, true), (row.AttemptsAllowed, row.AttemptsUsed, row.CanGrant));
        Assert.Equal([1, 2], row.Attempts.Select(a => a.Number));
        Assert.True(closed.WindowClosed);
        Assert.False(Assert.Single(closed.Candidates).CanGrant);
    }

    [Fact]
    public async Task TheStaffList_ForAnUnknownExam_IsNotFound()
    {
        var handler = new ListExamAttemptsHandler(_catalog, _roster, _attempts, _grants, _clock);

        await Assert.ThrowsAsync<ExamNotFoundError>(() => handler.HandleAsync(Guid.NewGuid(), CancellationToken.None));
    }

    // ---- what the candidate sees on My exams ---------------------------------------------------------------------------

    private async Task<MyExamDto> MyExamAsync(IEnumerable<Attempt> attempts, int granted)
    {
        _enrollments.GetEnrolledExamIdsAsync(_candidate, Arg.Any<CancellationToken>()).Returns([_exam.Id]);
        _catalog.FindPublishedAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([_exam]);
        _attempts.ListForCandidateAsync(_candidate, Arg.Any<CancellationToken>()).Returns(attempts.ToList());
        _grants.CountsForCandidateAsync(_candidate, Arg.Any<CancellationToken>())
            .Returns(granted > 0 ? new Dictionary<Guid, int> { [_exam.Id] = granted } : new Dictionary<Guid, int>());

        return Assert.Single(await new MyExamsHandler(_enrollments, _catalog, _attempts, _grants, _clock).HandleAsync(_candidate, CancellationToken.None));
    }

    [Fact]
    public async Task MyExams_ListsEveryAttempt_InOrder_AndTheLatestIsWhatTheOldFieldsMean()
    {
        var first = Made(score: 1m);
        var second = Made(score: 2m);

        var item = await MyExamAsync([second, first], granted: 1);

        Assert.Equal([1, 2], item.Attempts.Select(a => a.Number));
        Assert.Equal(second.Id, item.AttemptId);
        Assert.Equal(2m, item.Score);
        Assert.Equal((2, 2), (item.AttemptsAllowed, item.AttemptsUsed));
        Assert.False(item.CanStartAttempt);
    }

    [Fact]
    public async Task MyExams_OffersAnotherAttempt_OnlyWhenOneIsLeftAndNoneIsInProgressAndTheWindowIsOpen()
    {
        var first = Made();

        var none = await MyExamAsync([first], granted: 0);
        var granted = await MyExamAsync([first], granted: 1);

        Assert.False(none.CanStartAttempt);
        Assert.True(granted.CanStartAttempt);
        Assert.Equal(2, granted.AttemptsAllowed);

        var open = Made(open: true);
        Assert.False((await MyExamAsync([first, open], granted: 2)).CanStartAttempt);

        _clock.UtcNow = _exam.EndUtc.AddMinutes(1);
        Assert.False((await MyExamAsync([first], granted: 1)).CanStartAttempt);
    }

    [Fact]
    public async Task MyExams_WithoutAnAttempt_OffersTheFirstOne_AsItAlwaysDid()
    {
        var item = await MyExamAsync([], granted: 0);

        Assert.True(item.CanStartAttempt);
        Assert.Equal((1, 0), (item.AttemptsAllowed, item.AttemptsUsed));
        Assert.Empty(item.Attempts);
    }
}
