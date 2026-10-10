using ExamPlatform.Modules.Admin.Contracts;
using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Contracts;
using ExamPlatform.Modules.Identity.Contracts;
using ExamPlatform.Modules.Invite.Contracts;
using ExamPlatform.Modules.Proctoring.Application;
using ExamPlatform.Modules.Proctoring.Application.Commands;
using ExamPlatform.Modules.Proctoring.Application.Queries;
using ExamPlatform.Modules.Proctoring.Domain;
using ExamPlatform.Modules.Proctoring.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;
using NSubstitute;

namespace ExamPlatform.Modules.Proctoring.UnitTests;

/// <summary>The review queue and the two decisions a reviewer can make on a flag, each audited (FR-27, FR-40).</summary>
public class RiskFlagHandlerTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Reviewer = Guid.NewGuid();

    private readonly Guid _examId = Guid.NewGuid();
    private readonly IExamCatalog _catalog = Substitute.For<IExamCatalog>();
    private readonly IExamRoster _roster = Substitute.For<IExamRoster>();
    private readonly FakeRiskAssessmentRepository _assessments = new();
    private readonly IProctoringUnitOfWork _unitOfWork = Substitute.For<IProctoringUnitOfWork>();
    private readonly IAuditLogger _auditLogger = Substitute.For<IAuditLogger>();
    private readonly Clock _clock = Substitute.For<Clock>();
    private readonly IAttemptSignalSource _signals = Substitute.For<IAttemptSignalSource>();
    private readonly ICandidateAgeDirectory _ages = Substitute.For<ICandidateAgeDirectory>();
    private readonly ProctoringAuditTrail _audit;

    public RiskFlagHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _catalog.FindAsync(_examId, Arg.Any<CancellationToken>()).Returns(new ExamSnapshot(
            _examId, "Maths Final", null, true, Now, Now.AddHours(3), null, null, 1, 0, 0, []));
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(0);
        _audit = new ProctoringAuditTrail(_auditLogger, Substitute.For<IRequestContext>());
        _signals.ListFinishedAttemptRefsAsync(_examId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<FinishedAttemptRef>>([]));
    }

    /// <summary>The queue handler, with the minors switch set as the test needs it.</summary>
    private ListRiskFlagsHandler ListHandler(bool minorsScanEnabled = false) =>
        new(_catalog, _roster, _assessments, new AttemptScanScope(_signals, _ages, new MinorScanPolicy(minorsScanEnabled)), new MinorScanPolicy(minorsScanEnabled));

    private RiskAssessment Add(int focus = 0, int attemptNumber = 1, int changes = 0, bool invalidated = false)
    {
        var input = RiskTestData.Attempt(examId: _examId, focus: focus, number: attemptNumber, changes: changes, invalidated: invalidated);
        var assessment = RiskAssessment.Create(_examId, input, RiskScorer.Score(input, 0, RiskTestData.DefaultPolicy), Now);
        _assessments.Add(assessment);
        return assessment;
    }

    private void RosterHas(params (Guid Candidate, string Email)[] candidates) =>
        _roster.GetEnrolledCandidatesAsync(_examId, Arg.Any<CancellationToken>()).Returns(
            Task.FromResult<IReadOnlyList<EnrolledCandidate>>(candidates.Select(c => new EnrolledCandidate(c.Candidate, c.Email)).ToList()));

    [Fact]
    public async Task TheQueue_ShowsTheHighestScoreFirst_AndOnlyFlaggedAttempts()
    {
        var modest = Add(focus: 3);
        var severe = Add(focus: 3, attemptNumber: 2, changes: 2, invalidated: true);
        var quiet = Add(focus: 0, attemptNumber: 3);
        RosterHas((modest.CandidateId, "modest@example.com"));
        var handler = ListHandler();

        var queue = await handler.HandleAsync(_examId, RiskFlagFilter.Open, PageRequest.Create(null, null), CancellationToken.None);

        // The quiet attempt (score 0) is not flagged, so it is not in the queue at all.
        Assert.Equal(2, queue.Total);
        Assert.Equal(new[] { severe.Id, modest.Id }, queue.Items.Select(i => i.Id));
        Assert.DoesNotContain(queue.Items, i => i.Id == quiet.Id);
        Assert.Equal("Open", queue.Filter);
        Assert.Equal(5, queue.Items.First().Signals.Count);
    }

    [Fact]
    public async Task TheQueue_MapsTheRosterAddressOfACandidate()
    {
        var flagged = Add(focus: 3);
        RosterHas((flagged.CandidateId, "candidate@example.com"));
        var handler = ListHandler();

        var queue = await handler.HandleAsync(_examId, RiskFlagFilter.Open, PageRequest.Create(null, null), CancellationToken.None);

        Assert.Equal("candidate@example.com", queue.Items.Single().CandidateEmail);
    }

    [Fact]
    public async Task TheQueue_OmitsDecidedFlags_UnlessTheFilterAsksForThem()
    {
        var open = Add(focus: 3);
        var dismissed = Add(focus: 3, attemptNumber: 2);
        dismissed.Dismiss(Reviewer, "Confirmed with invigilator", Now);
        RosterHas();
        var handler = ListHandler();
        var page = PageRequest.Create(null, null);

        var awaiting = await handler.HandleAsync(_examId, RiskFlagFilter.Open, page, CancellationToken.None);
        var decided = await handler.HandleAsync(_examId, RiskFlagFilter.Dismissed, page, CancellationToken.None);
        var everything = await handler.HandleAsync(_examId, RiskFlagFilter.All, page, CancellationToken.None);

        Assert.Equal(open.Id, awaiting.Items.Single().Id);
        Assert.Equal(dismissed.Id, decided.Items.Single().Id);
        Assert.Equal("Confirmed with invigilator", decided.Items.Single().DecisionNote);
        Assert.Equal(2, everything.Total);
    }

    [Fact]
    public async Task TheQueue_PagesWithATotalAcrossAllPages()
    {
        for (var i = 1; i <= 3; i++)
            Add(focus: 3, attemptNumber: i);
        RosterHas();
        var handler = ListHandler();

        var second = await handler.HandleAsync(_examId, RiskFlagFilter.Open, PageRequest.Create(2, 2), CancellationToken.None);

        Assert.Equal(3, second.Total);
        Assert.Equal(2, second.PageSize);
        Assert.Single(second.Items);
    }

    [Fact]
    public async Task TheQueue_ForAnUnknownExam_IsNotFound()
    {
        _catalog.FindAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((ExamSnapshot?)null);
        var handler = ListHandler();

        await Assert.ThrowsAsync<ExamNotFoundError>(() =>
            handler.HandleAsync(Guid.NewGuid(), RiskFlagFilter.Open, PageRequest.Create(null, null), CancellationToken.None));
    }

    [Fact]
    public async Task AReviewer_MarksAFlagReviewed_AndTheAttemptIsAudited()
    {
        var flag = Add(focus: 3);
        var handler = new ReviewRiskFlagHandler(_assessments, _unitOfWork, _clock, _audit);

        await handler.HandleAsync(flag.Id, Reviewer, "Nothing further", CancellationToken.None);

        Assert.Equal(RiskFlagStatus.Reviewed, flag.Status);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _auditLogger.Received(1).RecordAsync(
            Arg.Is<AuditEntry>(e => e.Action == "Proctoring.RiskFlagReviewed" && e.EntityId == flag.Id.ToString() && e.ActorUserId == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ADismissal_WithoutANote_IsRefused_AndNothingIsSaved()
    {
        var flag = Add(focus: 3);
        var handler = new DismissRiskFlagHandler(_assessments, _unitOfWork, _clock, _audit);

        await Assert.ThrowsAsync<InvalidRiskDecisionError>(() => handler.HandleAsync(flag.Id, Reviewer, "  ", CancellationToken.None));

        Assert.Equal(RiskFlagStatus.Open, flag.Status);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _auditLogger.DidNotReceive().RecordAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ADismissal_WithANote_IsRecordedAndAudited()
    {
        var flag = Add(focus: 3);
        var handler = new DismissRiskFlagHandler(_assessments, _unitOfWork, _clock, _audit);

        await handler.HandleAsync(flag.Id, Reviewer, "Power cut at the centre; the invigilator confirmed it.", CancellationToken.None);

        Assert.Equal(RiskFlagStatus.Dismissed, flag.Status);
        await _auditLogger.Received(1).RecordAsync(
            Arg.Is<AuditEntry>(e => e.Action == "Proctoring.RiskFlagDismissed" && e.Metadata["note"] == "Power cut at the centre; the invigilator confirmed it."),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ADecisionOnAnUnknownFlag_IsNotFound()
    {
        var handler = new DismissRiskFlagHandler(_assessments, _unitOfWork, _clock, _audit);

        await Assert.ThrowsAsync<RiskAssessmentNotFoundError>(() =>
            handler.HandleAsync(Guid.NewGuid(), Reviewer, "A note", CancellationToken.None));
    }

    [Fact]
    public async Task ADecisionOnAnUnflaggedAttempt_IsRefused()
    {
        var input = RiskTestData.Attempt(examId: _examId);
        var quiet = RiskAssessment.Create(_examId, input, RiskScorer.Score(input, 0, RiskTestData.DefaultPolicy), Now);
        _assessments.Add(quiet);
        var handler = new ReviewRiskFlagHandler(_assessments, _unitOfWork, _clock, _audit);

        await Assert.ThrowsAsync<RiskFlagNotRaisedError>(() => handler.HandleAsync(quiet.Id, Reviewer, null, CancellationToken.None));
    }

    [Fact]
    public async Task TheQueue_StatesTheGap_WhenAnUnder18AttemptIsLeftOut()
    {
        // With minors not scanned, the queue says how many finished attempts were left out, so the gap is visible to staff.
        var minorAttempt = Guid.NewGuid();
        _signals.ListFinishedAttemptRefsAsync(_examId, Arg.Any<CancellationToken>()).Returns(
            Task.FromResult<IReadOnlyList<FinishedAttemptRef>>([new FinishedAttemptRef(minorAttempt, Guid.NewGuid(), Now)]));
        _ages.FindAttemptsSatAsMinorAsync(Arg.Any<IReadOnlyCollection<AttemptStart>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid> { minorAttempt }));
        RosterHas();

        var queue = await ListHandler(minorsScanEnabled: false).HandleAsync(_examId, RiskFlagFilter.Open, PageRequest.Create(null, null), CancellationToken.None);

        Assert.False(queue.MinorsScanEnabled);
        Assert.Equal(1, queue.ExcludedUnder18Attempts);
    }

    [Fact]
    public async Task TheQueue_StatesNoGap_WhenMinorsMayBeScanned()
    {
        _signals.ListFinishedAttemptRefsAsync(_examId, Arg.Any<CancellationToken>()).Returns(
            Task.FromResult<IReadOnlyList<FinishedAttemptRef>>([new FinishedAttemptRef(Guid.NewGuid(), Guid.NewGuid(), Now)]));
        RosterHas();

        var queue = await ListHandler(minorsScanEnabled: true).HandleAsync(_examId, RiskFlagFilter.Open, PageRequest.Create(null, null), CancellationToken.None);

        Assert.True(queue.MinorsScanEnabled);
        Assert.Equal(0, queue.ExcludedUnder18Attempts);
        await _ages.DidNotReceive().FindAttemptsSatAsMinorAsync(Arg.Any<IReadOnlyCollection<AttemptStart>>(), Arg.Any<CancellationToken>());
    }
}
