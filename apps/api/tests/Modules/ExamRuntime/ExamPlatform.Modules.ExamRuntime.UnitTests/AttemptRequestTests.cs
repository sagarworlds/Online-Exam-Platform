using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application;
using ExamPlatform.Modules.ExamRuntime.Application.Commands;
using ExamPlatform.Modules.ExamRuntime.Application.Dtos;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Application.Queries;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.Identity.Contracts;
using ExamPlatform.Modules.Invite.Contracts;
using NSubstitute;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>The request entity, which carries no dependencies.</summary>
public class AttemptRequestEntityTests
{
    private static AttemptRequest Pending(string? message = "My laptop crashed") =>
        AttemptRequest.Create(Guid.NewGuid(), Guid.NewGuid(), message, Fixtures.Now);

    [Fact]
    public void ANewRequest_IsPending_AndKeepsTheTrimmedMessage()
    {
        var request = Pending("  My laptop crashed  ");

        Assert.Equal(AttemptRequestStatus.Pending, request.Status);
        Assert.Equal("My laptop crashed", request.Message);
        Assert.Equal(Fixtures.Now, request.RequestedAtUtc);
        Assert.Null(request.DecidedAtUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankMessage_IsStoredAsNone(string? message) => Assert.Null(Pending(message).Message);

    [Fact]
    public void AMessageOverTheLimit_IsRefused()
    {
        Assert.Throws<InvalidAttemptError>(() => Pending(new string('x', AttemptRequest.MaxTextLength + 1)));
        Pending(new string('x', AttemptRequest.MaxTextLength));
    }

    [Fact]
    public void Approving_RecordsWhoAndWhen()
    {
        var request = Pending();
        var admin = Guid.NewGuid();

        request.Approve(admin, Fixtures.Now.AddHours(1));

        Assert.Equal(AttemptRequestStatus.Approved, request.Status);
        Assert.Equal(admin, request.DecidedByUserId);
        Assert.Equal(Fixtures.Now.AddHours(1), request.DecidedAtUtc);
    }

    [Fact]
    public void Declining_KeepsTheTrimmedNote()
    {
        var request = Pending();

        request.Decline(Guid.NewGuid(), Fixtures.Now, "  Please ask your teacher  ");

        Assert.Equal(AttemptRequestStatus.Declined, request.Status);
        Assert.Equal("Please ask your teacher", request.DecisionNote);
    }

    [Fact]
    public void ADecidedRequest_CannotBeDecidedAgain()
    {
        var approved = Pending();
        approved.Approve(Guid.NewGuid(), Fixtures.Now);
        var declined = Pending();
        declined.Decline(Guid.NewGuid(), Fixtures.Now, null);

        Assert.Throws<AttemptRequestNotPendingError>(() => approved.Decline(Guid.NewGuid(), Fixtures.Now, "no"));
        Assert.Throws<AttemptRequestNotPendingError>(() => declined.Approve(Guid.NewGuid(), Fixtures.Now));
        Assert.Equal(AttemptRequestStatus.Approved, approved.Status);
    }

    [Fact]
    public void ANoteOverTheLimit_IsRefused_AndTheRequestStaysPending()
    {
        var request = Pending();

        Assert.Throws<InvalidAttemptError>(() => request.Decline(Guid.NewGuid(), Fixtures.Now, new string('x', AttemptRequest.MaxTextLength + 1)));

        Assert.Equal(AttemptRequestStatus.Pending, request.Status);
    }
}

/// <summary>
/// A candidate who has used their attempt asks for another and an administrator answers (the flow as the handlers see it), with
/// the other modules replaced by fakes.
/// </summary>
public class AttemptRequestHandlerTests
{
    private readonly Guid _candidate = Guid.NewGuid();
    private readonly Guid _admin = Guid.NewGuid();
    private readonly FakeClock _clock = new(Fixtures.Now);
    private readonly IExamCatalog _catalog = Substitute.For<IExamCatalog>();
    private readonly IEnrollments _enrollments = Substitute.For<IEnrollments>();
    private readonly IExamRoster _roster = Substitute.For<IExamRoster>();
    private readonly IAttemptRepository _attempts = Substitute.For<IAttemptRepository>();
    private readonly IExtraAttemptGrantRepository _grants = Substitute.For<IExtraAttemptGrantRepository>();
    private readonly IAccommodationRepository _accommodations = Substitute.For<IAccommodationRepository>();
    private readonly IAttemptRequestRepository _requests = Substitute.For<IAttemptRequestRepository>();
    private readonly IExamRuntimeUnitOfWork _unitOfWork = Substitute.For<IExamRuntimeUnitOfWork>();
    private readonly IAttemptRequestNotifier _notifier = Substitute.For<IAttemptRequestNotifier>();
    private readonly IStaffDirectory _staff = Substitute.For<IStaffDirectory>();

    private ExamSnapshot _exam;
    private readonly List<Attempt> _theirs = [];
    private int _granted;
    private bool _pending;

    public AttemptRequestHandlerTests()
    {
        _exam = Fixtures.Exam([Fixtures.Question("First")]);
        _catalog.FindAsync(_exam.Id, Arg.Any<CancellationToken>()).Returns(_exam);
        _enrollments.IsEnrolledAsync(_candidate, _exam.Id, Arg.Any<CancellationToken>()).Returns(true);
        _roster.GetEnrolledCandidatesAsync(_exam.Id, Arg.Any<CancellationToken>())
            .Returns([new EnrolledCandidate(_candidate, "student@example.com")]);
        _attempts.ListForCandidateAtExamAsync(_exam.Id, _candidate, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyList<Attempt>>(_theirs.OrderBy(a => a.Number).ToList()));
        _grants.CountAsync(_exam.Id, _candidate, Arg.Any<CancellationToken>()).Returns(_ => _granted);
        _grants.When(g => g.Add(Arg.Any<ExtraAttemptGrant>())).Do(_ => _granted++);
        _requests.HasPendingAsync(_exam.Id, _candidate, Arg.Any<CancellationToken>()).Returns(_ => _pending);
        _notifier.SendDecisionAsync(Arg.Any<AttemptRequestDecisionEmail>(), Arg.Any<CancellationToken>()).Returns(true);
        _notifier.SendNewRequestAsync(Arg.Any<NewAttemptRequestEmail>(), Arg.Any<CancellationToken>()).Returns(true);
        _staff.GetEmailsWithPermissionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(["admin1@example.com", "admin2@example.com"]));
    }

    private RequestAttemptHandler Request => new(_catalog, _enrollments, _attempts, _grants, _requests, _unitOfWork, _staff, _roster, _notifier, _clock);
    private AttemptRequestDtoFactory Dtos => new(_catalog, _roster);
    private ApproveAttemptRequestHandler Approve =>
        new(_requests, new GrantExtraAttemptHandler(_catalog, _roster, _attempts, _grants, _accommodations, _unitOfWork, _clock), Dtos, _notifier);
    private DeclineAttemptRequestHandler Decline => new(_requests, _unitOfWork, Dtos, _clock, _notifier);

    private Attempt Made(bool open = false)
    {
        var started = Fixtures.Now.AddMinutes(-20);
        var attempt = Attempt.Start(_exam.Id, _candidate, _theirs.Count + 1, started, started.AddMinutes(30));
        if (!open)
            attempt.Submit(started.AddMinutes(10), 1m, 2m);
        _theirs.Add(attempt);
        return attempt;
    }

    private AttemptRequest Waiting(string? message = "Power cut")
    {
        var request = AttemptRequest.Create(_exam.Id, _candidate, message, Fixtures.Now.AddMinutes(-5));
        _requests.GetByIdAsync(request.Id, Arg.Any<CancellationToken>()).Returns(request);
        return request;
    }

    // ---- asking ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Request_AfterTheOnlyAttemptIsUsed_RecordsAPendingRequest()
    {
        Made();

        var dto = await Request.HandleAsync(_exam.Id, _candidate, "  Power cut  ", CancellationToken.None);

        Assert.Equal(AttemptRequestStatus.Pending, dto.Status);
        Assert.Equal("Power cut", dto.Message);
        _requests.Received(1).Add(Arg.Is<AttemptRequest>(r => r.ExamId == _exam.Id && r.CandidateId == _candidate && r.Status == AttemptRequestStatus.Pending));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Request_BeforeTheAttemptIsUsed_IsRefused()
    {
        await Assert.ThrowsAsync<AttemptNotNeededError>(() => Request.HandleAsync(_exam.Id, _candidate, null, CancellationToken.None));

        _requests.DidNotReceive().Add(Arg.Any<AttemptRequest>());
    }

    [Fact]
    public async Task Request_WhileAnAttemptIsInProgress_IsRefused()
    {
        _granted = 1;
        Made();
        Made(open: true);

        await Assert.ThrowsAsync<AttemptNotNeededError>(() => Request.HandleAsync(_exam.Id, _candidate, null, CancellationToken.None));
    }

    [Fact]
    public async Task Request_WithOneAlreadyWaiting_IsRefused()
    {
        Made();
        _pending = true;

        await Assert.ThrowsAsync<AttemptRequestPendingError>(() => Request.HandleAsync(_exam.Id, _candidate, null, CancellationToken.None));

        _requests.DidNotReceive().Add(Arg.Any<AttemptRequest>());
    }

    [Fact]
    public async Task Request_ByAnUnenrolledCandidate_IsReportedLikeAMissingExam()
    {
        await Assert.ThrowsAsync<ExamNotAvailableError>(() => Request.HandleAsync(_exam.Id, Guid.NewGuid(), null, CancellationToken.None));
    }

    [Fact]
    public async Task Request_AfterTheWindowHasClosed_IsRefused()
    {
        Made();
        _clock.UtcNow = Fixtures.Now.AddDays(1);

        await Assert.ThrowsAsync<ExamClosedError>(() => Request.HandleAsync(_exam.Id, _candidate, null, CancellationToken.None));
    }

    [Fact]
    public async Task Request_WhenTheLimitWasLoweredBelowTheirAttempts_IsRefused()
    {
        Made();
        Made();

        await Assert.ThrowsAsync<AttemptOverLimitError>(() => Request.HandleAsync(_exam.Id, _candidate, null, CancellationToken.None));
    }

    // ---- telling the people who can answer --------------------------------------------------------------------------

    [Fact]
    public async Task Request_TellsEveryoneWhoCanManageExams_WhoAskedAndWhy()
    {
        Made();

        await Request.HandleAsync(_exam.Id, _candidate, "Power cut", CancellationToken.None);

        await _staff.Received(1).GetEmailsWithPermissionAsync("exam.manage", Arg.Any<CancellationToken>());
        foreach (var manager in new[] { "admin1@example.com", "admin2@example.com" })
        {
            await _notifier.Received(1).SendNewRequestAsync(
                Arg.Is<NewAttemptRequestEmail>(e => e.To == manager && e.ExamName == "Physics" && e.CandidateEmail == "student@example.com" && e.Message == "Power cut"),
                Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task Request_WithNoOneToTell_StillRecordsTheRequest()
    {
        Made();
        _staff.GetEmailsWithPermissionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<string>>([]));

        var dto = await Request.HandleAsync(_exam.Id, _candidate, null, CancellationToken.None);

        Assert.Equal(AttemptRequestStatus.Pending, dto.Status);
        _requests.Received(1).Add(Arg.Any<AttemptRequest>());
        await _notifier.DidNotReceive().SendNewRequestAsync(Arg.Any<NewAttemptRequestEmail>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Request_WhenTheEmailsCannotBeSent_IsStillRecordedAndAnswered()
    {
        Made();
        _notifier.SendNewRequestAsync(Arg.Any<NewAttemptRequestEmail>(), Arg.Any<CancellationToken>()).Returns(false);

        var dto = await Request.HandleAsync(_exam.Id, _candidate, null, CancellationToken.None);

        Assert.Equal(AttemptRequestStatus.Pending, dto.Status);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Request_ThatIsRefused_TellsNobody()
    {
        // Still has the attempt they hold, so there is nothing to ask for yet.
        await Assert.ThrowsAsync<AttemptNotNeededError>(() => Request.HandleAsync(_exam.Id, _candidate, null, CancellationToken.None));

        await _staff.DidNotReceive().GetEmailsWithPermissionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _notifier.DidNotReceive().SendNewRequestAsync(Arg.Any<NewAttemptRequestEmail>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Request_ByACandidateWhoCannotBeFoundOnTheRoster_StillTellsThemAsAnUnnamedCandidate()
    {
        Made();
        _roster.GetEnrolledCandidatesAsync(_exam.Id, Arg.Any<CancellationToken>()).Returns([]);

        await Request.HandleAsync(_exam.Id, _candidate, null, CancellationToken.None);

        await _notifier.Received(2).SendNewRequestAsync(Arg.Is<NewAttemptRequestEmail>(e => e.CandidateEmail == null), Arg.Any<CancellationToken>());
    }

    // ---- answering -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Approve_GrantsTheAttempt_AndMarksTheRequestApproved_InOneSave()
    {
        Made();
        var request = Waiting();

        var dto = await Approve.HandleAsync(request.Id, _admin, CancellationToken.None);

        Assert.Equal(AttemptRequestStatus.Approved, request.Status);
        Assert.Equal(_admin, request.DecidedByUserId);
        Assert.Equal(AttemptRequestStatus.Approved, dto.Status);
        Assert.Equal("student@example.com", dto.CandidateEmail);
        Assert.Equal("Physics", dto.ExamName);
        _grants.Received(1).Add(Arg.Is<ExtraAttemptGrant>(g => g.CandidateId == _candidate && g.Number == 1 && g.GrantedByUserId == _admin && g.Reason == "Power cut"));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Approve_OfAnUnknownRequest_IsNotFound()
    {
        await Assert.ThrowsAsync<AttemptRequestNotFoundError>(() => Approve.HandleAsync(Guid.NewGuid(), _admin, CancellationToken.None));
    }

    [Fact]
    public async Task Approve_OfADecidedRequest_GrantsNothing()
    {
        Made();
        var request = Waiting();
        request.Decline(_admin, Fixtures.Now, null);

        await Assert.ThrowsAsync<AttemptRequestNotPendingError>(() => Approve.HandleAsync(request.Id, _admin, CancellationToken.None));

        _grants.DidNotReceive().Add(Arg.Any<ExtraAttemptGrant>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Approve_WhenTheCandidateAlreadyHasAnAttemptLeft_ChangesNothing_SoTheRequestCanBeDeclined()
    {
        Made();
        _granted = 1; // an administrator already gave one directly
        var request = Waiting();

        await Assert.ThrowsAsync<AttemptAvailableError>(() => Approve.HandleAsync(request.Id, _admin, CancellationToken.None));

        Assert.Equal(AttemptRequestStatus.Pending, request.Status);
        _grants.DidNotReceive().Add(Arg.Any<ExtraAttemptGrant>());
    }

    [Fact]
    public async Task Decline_MarksTheRequestDeclined_WithTheNote_AndGrantsNothing()
    {
        var request = Waiting();

        var dto = await Decline.HandleAsync(request.Id, _admin, "Speak to your teacher", CancellationToken.None);

        Assert.Equal(AttemptRequestStatus.Declined, dto.Status);
        Assert.Equal("Speak to your teacher", dto.DecisionNote);
        _grants.DidNotReceive().Add(Arg.Any<ExtraAttemptGrant>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ---- telling the candidate -------------------------------------------------------------------------------------

    [Fact]
    public async Task Approve_EmailsTheCandidate_AndSaysSo()
    {
        Made();
        var request = Waiting();

        var dto = await Approve.HandleAsync(request.Id, _admin, CancellationToken.None);

        Assert.True(dto.CandidateNotified);
        await _notifier.Received(1).SendDecisionAsync(
            Arg.Is<AttemptRequestDecisionEmail>(e => e.To == "student@example.com" && e.ExamName == "Physics" && e.Approved), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Decline_EmailsTheCandidateTheNote()
    {
        var request = Waiting();

        var dto = await Decline.HandleAsync(request.Id, _admin, "Speak to your teacher", CancellationToken.None);

        Assert.True(dto.CandidateNotified);
        await _notifier.Received(1).SendDecisionAsync(
            Arg.Is<AttemptRequestDecisionEmail>(e => !e.Approved && e.Note == "Speak to your teacher"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WhenTheEmailCannotBeSent_TheDecisionStands_AndTheAdministratorIsToldTheyWereNotNotified()
    {
        _notifier.SendDecisionAsync(Arg.Any<AttemptRequestDecisionEmail>(), Arg.Any<CancellationToken>()).Returns(false);
        var request = Waiting();

        var dto = await Decline.HandleAsync(request.Id, _admin, null, CancellationToken.None);

        Assert.False(dto.CandidateNotified);
        Assert.Equal(AttemptRequestStatus.Declined, request.Status);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ACandidateWhoIsNoLongerEnrolled_IsNotEmailed_AndTheAdministratorIsToldSo()
    {
        _roster.GetEnrolledCandidatesAsync(_exam.Id, Arg.Any<CancellationToken>()).Returns([]);
        var request = Waiting();

        var dto = await Decline.HandleAsync(request.Id, _admin, null, CancellationToken.None);

        Assert.False(dto.CandidateNotified);
        await _notifier.DidNotReceive().SendDecisionAsync(Arg.Any<AttemptRequestDecisionEmail>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Approve_ThatIsRefused_SendsNoEmail()
    {
        Made();
        _granted = 1;
        var request = Waiting();

        await Assert.ThrowsAsync<AttemptAvailableError>(() => Approve.HandleAsync(request.Id, _admin, CancellationToken.None));

        await _notifier.DidNotReceive().SendDecisionAsync(Arg.Any<AttemptRequestDecisionEmail>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Decline_OfAnUnknownRequest_IsNotFound()
    {
        await Assert.ThrowsAsync<AttemptRequestNotFoundError>(() => Decline.HandleAsync(Guid.NewGuid(), _admin, null, CancellationToken.None));
    }

    [Fact]
    public async Task List_DefaultsToWaitingRequests_AndNamesTheExamAndCandidate()
    {
        var request = Waiting();
        _requests.ListAsync(AttemptRequestStatus.Pending, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AttemptRequest>>([request]));

        var list = await new ListAttemptRequestsHandler(_requests, Dtos).HandleAsync(null, CancellationToken.None);

        var row = Assert.Single(list);
        Assert.Equal("Physics", row.ExamName);
        Assert.Equal("student@example.com", row.CandidateEmail);
        Assert.Equal("Power cut", row.Message);
    }

    // ---- what the candidate sees -----------------------------------------------------------------------------------

    private async Task<MyExamDto> MyExamAsync(AttemptRequest? latest = null)
    {
        _enrollments.GetEnrolledExamIdsAsync(_candidate, Arg.Any<CancellationToken>()).Returns([_exam.Id]);
        _catalog.FindPublishedAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ExamSnapshot>>([_exam]));
        _attempts.ListForCandidateAsync(_candidate, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<Attempt>>(_theirs.ToList()));
        _grants.CountsForCandidateAsync(_candidate, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyDictionary<Guid, int>>(new Dictionary<Guid, int> { [_exam.Id] = _granted }));
        _requests.LatestForCandidateAsync(_candidate, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyDictionary<Guid, AttemptRequest>>(latest is null ? new Dictionary<Guid, AttemptRequest>() : new Dictionary<Guid, AttemptRequest> { [_exam.Id] = latest }));

        return Assert.Single(await new MyExamsHandler(_enrollments, _catalog, _attempts, _grants, _requests, _accommodations, _clock).HandleAsync(_candidate, CancellationToken.None));
    }

    [Fact]
    public async Task MyExams_OffersARequest_OnceTheAttemptIsUsed()
    {
        Made();

        var exam = await MyExamAsync();

        Assert.True(exam.CanRequestAttempt);
        Assert.Null(exam.AttemptRequest);
    }

    [Fact]
    public async Task MyExams_DoesNotOfferARequest_BeforeTheAttemptIsUsed()
    {
        Assert.False((await MyExamAsync()).CanRequestAttempt);
    }

    [Fact]
    public async Task MyExams_ShowsAWaitingRequest_AndOffersNoSecondOne()
    {
        Made();

        var exam = await MyExamAsync(Waiting());

        Assert.False(exam.CanRequestAttempt);
        Assert.Equal(AttemptRequestStatus.Pending, exam.AttemptRequest!.Status);
    }

    [Fact]
    public async Task MyExams_ShowsADeclinedRequestWithItsNote_AndAllowsAskingAgain()
    {
        Made();
        var request = Waiting();
        request.Decline(_admin, Fixtures.Now, "Not this time");

        var exam = await MyExamAsync(request);

        Assert.True(exam.CanRequestAttempt);
        Assert.Equal("Not this time", exam.AttemptRequest!.DecisionNote);
    }

    [Fact]
    public async Task MyExams_ShowsAnApprovedRequest_AsAnAttemptTheyCanStart()
    {
        Made();
        var request = Waiting();
        request.Approve(_admin, Fixtures.Now);
        _granted = 1;

        var exam = await MyExamAsync(request);

        Assert.True(exam.CanStartAttempt);
        Assert.False(exam.CanRequestAttempt);
        Assert.Equal(AttemptRequestStatus.Approved, exam.AttemptRequest!.Status);
    }
}
