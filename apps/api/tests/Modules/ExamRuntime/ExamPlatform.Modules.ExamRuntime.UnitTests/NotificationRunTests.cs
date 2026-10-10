using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.Invite.Contracts;
using ExamPlatform.Modules.Notifications.Contracts;
using NSubstitute;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>The platform's scheduled e-mails (FR-39): which are due, to whom, and that none goes twice.</summary>
public class NotificationRunTests
{
    private static readonly DateTime Now = Fixtures.Now;

    private readonly Guid _candidate = Guid.NewGuid();
    private readonly IExamCatalog _catalog = Substitute.For<IExamCatalog>();
    private readonly IExamRoster _roster = Substitute.For<IExamRoster>();
    private readonly IExamNotificationMailer _mailer = Substitute.For<IExamNotificationMailer>();
    private readonly IExamRuntimeUnitOfWork _unitOfWork = Substitute.For<IExamRuntimeUnitOfWork>();
    private readonly IInAppNotifier _inApp = Substitute.For<IInAppNotifier>();
    private readonly InMemoryDeliveries _deliveries = new();
    private readonly InMemoryQueries _queries;
    private readonly NotificationRun _run;

    public NotificationRunTests()
    {
        _queries = new InMemoryQueries(_deliveries);
        _mailer.IsAvailable.Returns(true);
        _mailer.SendReminderAsync(default!, default).ReturnsForAnyArgs(true);
        _mailer.SendResultReleasedAsync(default!, default).ReturnsForAnyArgs(true);
        _mailer.SendScoreRevisedAsync(default!, default).ReturnsForAnyArgs(true);
        _catalog.FindPublishedStartingBetweenAsync(default, default, default).ReturnsForAnyArgs([]);
        _inApp.NotifyManyAsync(Arg.Any<IReadOnlyCollection<InAppNotice>>(), Arg.Any<CancellationToken>()).Returns(true);
        _run = new NotificationRun(_catalog, _roster, _queries, _deliveries, _mailer, _unitOfWork, _inApp);
    }

    // ---- fakes of the two stores -----------------------------------------------------------------------

    private sealed class InMemoryDeliveries : INotificationDeliveryRepository
    {
        public List<NotificationDelivery> All { get; } = [];

        public void Add(NotificationDelivery delivery) => All.Add(delivery);

        public Task<IReadOnlyList<NotificationDelivery>> ListAsync(NotificationKind kind, IReadOnlyCollection<Guid> subjectIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<NotificationDelivery>>(All.Where(d => d.Kind == kind && subjectIds.Contains(d.SubjectId)).ToList());
    }

    /// <summary>Answers like the real queries: what is asked for, less what is already settled.</summary>
    private sealed class InMemoryQueries(InMemoryDeliveries deliveries) : INotificationQueries
    {
        public List<SubmittedAttemptRow> Attempts { get; } = [];

        public List<RevisionRow> Revisions { get; } = [];

        private bool Settled(NotificationKind kind, Guid subject, Guid recipient) =>
            deliveries.All.Any(d => d.Kind == kind && d.SubjectId == subject && d.RecipientId == recipient && d.IsSettled);

        public Task<IReadOnlyList<SubmittedAttemptRow>> ListAwaitingResultNoticeAsync(DateTime submittedAfterUtc, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SubmittedAttemptRow>>(
                Attempts.Where(a => a.SubmittedAtUtc > submittedAfterUtc && !Settled(NotificationKind.ResultReleased, a.AttemptId, a.CandidateId)).ToList());

        public Task<IReadOnlyList<RevisionRow>> ListAwaitingRevisionNoticeAsync(DateTime revisedAfterUtc, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RevisionRow>>(
                Revisions.Where(r => r.RevisedAtUtc > revisedAfterUtc && !Settled(NotificationKind.ScoreRevised, r.RevisionId, r.CandidateId)).ToList());
    }

    // ---- helpers ---------------------------------------------------------------------------------------

    private ExamSnapshot ExamStartingIn(TimeSpan lead, bool enrolled = true)
    {
        var exam = Fixtures.Exam([Fixtures.Question()], start: Now + lead, end: Now + lead + TimeSpan.FromHours(3));
        Knows(exam, enrolled);
        _catalog.FindPublishedStartingBetweenAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(call => (IReadOnlyList<ExamSnapshot>)new[] { exam }.Where(e => e.StartUtc > call.ArgAt<DateTime>(0) && e.StartUtc <= call.ArgAt<DateTime>(1)).ToList());
        return exam;
    }

    private void Knows(ExamSnapshot exam, bool enrolled = true)
    {
        _catalog.FindAsync(exam.Id, Arg.Any<CancellationToken>()).Returns(exam);
        _roster.GetEnrolledCandidatesAsync(exam.Id, Arg.Any<CancellationToken>())
            .Returns(enrolled ? [new EnrolledCandidate(_candidate, "asha@example.com")] : []);
    }

    private SubmittedAttemptRow Submitted(ExamSnapshot exam, DateTime submittedAt, int number = 1) =>
        new(Guid.NewGuid(), exam.Id, _candidate, number, submittedAt);

    // ---- no mail server --------------------------------------------------------------------------------

    [Fact]
    public async Task WithNoMailServer_NoEmailIsSentOrRecorded_ButTheFeedIsStillFilled()
    {
        _mailer.IsAvailable.Returns(false);
        var exam = ExamStartingIn(TimeSpan.FromHours(10));

        var summary = await _run.RunAsync(Now, CancellationToken.None);

        Assert.False(summary.MailAvailable);
        Assert.Empty(_deliveries.All);
        await _mailer.DidNotReceiveWithAnyArgs().SendReminderAsync(default!, default);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
        await _inApp.Received(1).NotifyManyAsync(
            Arg.Is<IReadOnlyCollection<InAppNotice>>(n => n.Count == 1
                && n.Single().RecipientUserId == _candidate
                && n.Single().Kind == InAppNoticeKind.ExamReminder24Hours
                && n.Single().SubjectId == exam.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnExamWithinTheHour_FeedsTheHourReminder_NotTheDayOne()
    {
        var exam = ExamStartingIn(TimeSpan.FromMinutes(40));

        await _run.RunAsync(Now, CancellationToken.None);

        await _inApp.Received(1).NotifyManyAsync(
            Arg.Is<IReadOnlyCollection<InAppNotice>>(n => n.Count == 1 && n.Single().Kind == InAppNoticeKind.ExamReminderOneHour && n.Single().SubjectId == exam.Id),
            Arg.Any<CancellationToken>());
    }

    // ---- reminders -------------------------------------------------------------------------------------

    [Fact]
    public async Task AnExamStartingWithinADay_RemindsEachEnrolledCandidate_WithTheNameAndStart()
    {
        var exam = ExamStartingIn(TimeSpan.FromHours(10));

        var summary = await _run.RunAsync(Now, CancellationToken.None);

        Assert.Equal(1, summary.RemindersSent);
        await _mailer.Received(1).SendReminderAsync(
            Arg.Is<ExamReminderEmail>(e => e.To == "asha@example.com" && e.ExamName == exam.Name && e.StartUtc == exam.StartUtc && !e.WithinAnHour),
            Arg.Any<CancellationToken>());
        var delivery = Assert.Single(_deliveries.All);
        Assert.Equal(NotificationKind.ExamReminder24Hours, delivery.Kind);
        Assert.Equal(exam.Id, delivery.SubjectId);
        Assert.Equal(_candidate, delivery.RecipientId);
        Assert.NotNull(delivery.SentAtUtc);
        await _unitOfWork.Received().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnExamStartingWithinTheHour_GetsTheCloserReminder_AndNotTheDayOne()
    {
        ExamStartingIn(TimeSpan.FromMinutes(40));

        var summary = await _run.RunAsync(Now, CancellationToken.None);

        Assert.Equal(1, summary.RemindersSent);
        await _mailer.Received(1).SendReminderAsync(Arg.Is<ExamReminderEmail>(e => e.WithinAnHour), Arg.Any<CancellationToken>());
        Assert.Equal(NotificationKind.ExamReminderOneHour, Assert.Single(_deliveries.All).Kind);
    }

    [Fact]
    public async Task ACandidateWhoWasRemindedAtADay_IsRemindedAgainWithinTheHour()
    {
        var exam = ExamStartingIn(TimeSpan.FromHours(10));
        await _run.RunAsync(Now, CancellationToken.None);

        var summary = await _run.RunAsync(Now + TimeSpan.FromHours(9.5), CancellationToken.None);

        Assert.Equal(1, summary.RemindersSent);
        Assert.Equal(
            [NotificationKind.ExamReminder24Hours, NotificationKind.ExamReminderOneHour],
            _deliveries.All.Select(d => d.Kind).Order().ToArray());
        Assert.All(_deliveries.All, d => Assert.Equal(exam.Id, d.SubjectId));
    }

    [Fact]
    public async Task RunningAgain_SendsNothingTwice()
    {
        ExamStartingIn(TimeSpan.FromHours(10));
        await _run.RunAsync(Now, CancellationToken.None);

        var summary = await _run.RunAsync(Now.AddMinutes(5), CancellationToken.None);

        Assert.Equal(0, summary.RemindersSent);
        await _mailer.Received(1).SendReminderAsync(Arg.Any<ExamReminderEmail>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnExamMoreThanADayAway_IsNotRemindedYet()
    {
        ExamStartingIn(TimeSpan.FromHours(30));

        var summary = await _run.RunAsync(Now, CancellationToken.None);

        Assert.Equal(0, summary.RemindersSent);
        Assert.Empty(_deliveries.All);
    }

    [Fact]
    public async Task AnExamWithNoOneEnrolled_RemindsNoOne()
    {
        ExamStartingIn(TimeSpan.FromHours(10), enrolled: false);

        var summary = await _run.RunAsync(Now, CancellationToken.None);

        Assert.Equal(0, summary.RemindersSent);
        await _mailer.DidNotReceiveWithAnyArgs().SendReminderAsync(default!, default);
    }

    [Fact]
    public async Task ACandidateWithNoAddress_IsSkipped()
    {
        var exam = ExamStartingIn(TimeSpan.FromHours(10));
        _roster.GetEnrolledCandidatesAsync(exam.Id, Arg.Any<CancellationToken>()).Returns([new EnrolledCandidate(_candidate, "  ")]);

        var summary = await _run.RunAsync(Now, CancellationToken.None);

        Assert.Equal(0, summary.RemindersSent);
        await _mailer.DidNotReceiveWithAnyArgs().SendReminderAsync(default!, default);
    }

    [Fact]
    public async Task AReminderTheMailServerRefuses_IsCounted_NotMarkedSent_AndRetriedOnlyAfterTheDelay()
    {
        ExamStartingIn(TimeSpan.FromHours(10));
        _mailer.SendReminderAsync(default!, default).ReturnsForAnyArgs(false);

        var first = await _run.RunAsync(Now, CancellationToken.None);
        var tooSoon = await _run.RunAsync(Now.AddMinutes(5), CancellationToken.None);

        Assert.Equal(0, first.RemindersSent);
        Assert.Equal(1, first.NotSent);
        Assert.Equal(0, tooSoon.NotSent);
        var delivery = Assert.Single(_deliveries.All);
        Assert.Null(delivery.SentAtUtc);
        Assert.Equal(1, delivery.Attempts);

        _mailer.SendReminderAsync(default!, default).ReturnsForAnyArgs(true);
        var retry = await _run.RunAsync(Now + NotificationDelivery.RetryAfter, CancellationToken.None);

        Assert.Equal(1, retry.RemindersSent);
        Assert.NotNull(delivery.SentAtUtc);
        Assert.Equal(2, delivery.Attempts);
    }

    [Fact]
    public async Task AReminderThatCannotBeSentEnoughTimes_IsGivenUpOn()
    {
        ExamStartingIn(TimeSpan.FromHours(10));
        _mailer.SendReminderAsync(default!, default).ReturnsForAnyArgs(false);

        for (var i = 0; i < NotificationDelivery.MaxAttempts + 2; i++)
            await _run.RunAsync(Now + (i * NotificationDelivery.RetryAfter), CancellationToken.None);

        await _mailer.Received(NotificationDelivery.MaxAttempts).SendReminderAsync(Arg.Any<ExamReminderEmail>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ARosterThatCannotBeRead_IsReported_AndDoesNotStopTheOtherNotices()
    {
        var exam = ExamStartingIn(TimeSpan.FromHours(10));
        _roster.GetEnrolledCandidatesAsync(exam.Id, Arg.Any<CancellationToken>()).Returns<Task<IReadOnlyList<EnrolledCandidate>>>(_ => throw new InvalidOperationException("roster down"));
        var released = Fixtures.Exam([Fixtures.Question()], start: Now.AddHours(-2), end: Now.AddHours(-1));
        Knows(released);
        _queries.Attempts.Add(Submitted(released, Now.AddMinutes(-30)));
        Exception? reported = null;

        var summary = await _run.RunAsync(Now, CancellationToken.None, e => reported = e);

        Assert.Equal(1, summary.Errors);
        Assert.Equal("roster down", reported?.Message);
        Assert.Equal(0, summary.RemindersSent);
        Assert.Equal(1, summary.ResultNoticesSent);
    }

    // ---- released results ------------------------------------------------------------------------------

    [Fact]
    public async Task AnInstantResult_IsAnnouncedOnce_ToTheCandidateWhoSatIt()
    {
        var exam = Fixtures.Exam([Fixtures.Question()], start: Now.AddHours(-2), end: Now.AddHours(1));
        Knows(exam);
        var attempt = Submitted(exam, Now.AddMinutes(-10), number: 2);
        _queries.Attempts.Add(attempt);

        var first = await _run.RunAsync(Now, CancellationToken.None);
        var second = await _run.RunAsync(Now.AddMinutes(5), CancellationToken.None);

        Assert.Equal(1, first.ResultNoticesSent);
        Assert.Equal(0, second.ResultNoticesSent);
        await _mailer.Received(1).SendResultReleasedAsync(
            Arg.Is<ResultReleasedEmail>(e => e.To == "asha@example.com" && e.ExamName == exam.Name && e.AttemptNumber == 2), Arg.Any<CancellationToken>());
        var delivery = Assert.Single(_deliveries.All);
        Assert.Equal(NotificationKind.ResultReleased, delivery.Kind);
        Assert.Equal(attempt.AttemptId, delivery.SubjectId);
    }

    [Fact]
    public async Task AResultThatIsNotOutYet_IsNotAnnounced_UntilItIs()
    {
        var releaseAt = Now.AddHours(3);
        var exam = Fixtures.Exam(
            [Fixtures.Question()], start: Now.AddHours(-2), end: Now.AddHours(-1), resultRelease: ExamResultReleaseMode.Manual, resultReleaseTime: releaseAt);
        Knows(exam);
        _queries.Attempts.Add(Submitted(exam, Now.AddHours(-1)));

        var before = await _run.RunAsync(Now, CancellationToken.None);
        var after = await _run.RunAsync(releaseAt.AddMinutes(1), CancellationToken.None);

        Assert.Equal(0, before.ResultNoticesSent);
        Assert.Equal(1, after.ResultNoticesSent);
    }

    [Fact]
    public async Task AManualReleaseWithNoTimeYet_IsNotAnnounced()
    {
        var exam = Fixtures.Exam([Fixtures.Question()], start: Now.AddHours(-2), end: Now.AddHours(-1), resultRelease: ExamResultReleaseMode.Manual);
        Knows(exam);
        _queries.Attempts.Add(Submitted(exam, Now.AddHours(-1)));

        var summary = await _run.RunAsync(Now, CancellationToken.None);

        Assert.Equal(0, summary.ResultNoticesSent);
    }

    [Fact]
    public async Task AResultReleasedMoreThanADayAgo_IsLeftAlone_SoSwitchingThisOnDoesNotMailOldResults()
    {
        var exam = Fixtures.Exam([Fixtures.Question()], start: Now.AddDays(-5), end: Now.AddDays(-4));
        Knows(exam);
        _queries.Attempts.Add(Submitted(exam, Now.AddDays(-3)));
        _queries.Attempts.Add(Submitted(exam, Now - NotificationRun.AnnounceWithin + TimeSpan.FromMinutes(1)));

        var summary = await _run.RunAsync(Now, CancellationToken.None);

        Assert.Equal(1, summary.ResultNoticesSent);
    }

    [Fact]
    public async Task ACandidateNoLongerEnrolled_OrAnExamThatIsGone_IsSkipped()
    {
        var enrolledNoMore = Fixtures.Exam([Fixtures.Question()], start: Now.AddHours(-2), end: Now.AddHours(1));
        Knows(enrolledNoMore, enrolled: false);
        var gone = Fixtures.Exam([Fixtures.Question()], start: Now.AddHours(-2), end: Now.AddHours(1));
        _catalog.FindAsync(gone.Id, Arg.Any<CancellationToken>()).Returns((ExamSnapshot?)null);
        _queries.Attempts.Add(Submitted(enrolledNoMore, Now.AddMinutes(-5)));
        _queries.Attempts.Add(Submitted(gone, Now.AddMinutes(-5)));

        var summary = await _run.RunAsync(Now, CancellationToken.None);

        Assert.Equal(0, summary.ResultNoticesSent);
        Assert.Equal(0, summary.Errors);
        await _mailer.DidNotReceiveWithAnyArgs().SendResultReleasedAsync(default!, default);
    }

    // ---- revised scores --------------------------------------------------------------------------------

    private RevisionRow Revision(ExamSnapshot exam, DateTime submittedAt, DateTime revisedAt) =>
        new(Guid.NewGuid(), Guid.NewGuid(), exam.Id, _candidate, submittedAt, 0m, 1m, 1m, 1m, "Paris is the capital of France", revisedAt);

    [Fact]
    public async Task ARevisedScore_IsAnnouncedOnce_WithWhatChangedAndWhy()
    {
        var exam = Fixtures.Exam([Fixtures.Question()], start: Now.AddHours(-3), end: Now.AddHours(-2));
        Knows(exam);
        var revision = Revision(exam, Now.AddHours(-2), Now.AddMinutes(-20));
        _queries.Revisions.Add(revision);

        var first = await _run.RunAsync(Now, CancellationToken.None);
        var second = await _run.RunAsync(Now.AddMinutes(5), CancellationToken.None);

        Assert.Equal(1, first.RevisionNoticesSent);
        Assert.Equal(0, second.RevisionNoticesSent);
        await _mailer.Received(1).SendScoreRevisedAsync(
            Arg.Is<ScoreRevisedEmail>(e =>
                e.To == "asha@example.com" && e.ExamName == exam.Name && e.PreviousScore == 0m && e.NewScore == 1m && e.NewMaxScore == 1m
                && e.Reason == "Paris is the capital of France"),
            Arg.Any<CancellationToken>());
        Assert.Equal(revision.RevisionId, Assert.Single(_deliveries.All).SubjectId);
    }

    [Fact]
    public async Task AScoreRevisedBeforeTheCandidateCouldSeeTheResult_IsNotNews()
    {
        var releaseAt = Now.AddHours(5);
        var exam = Fixtures.Exam(
            [Fixtures.Question()], start: Now.AddHours(-3), end: Now.AddHours(-2), resultRelease: ExamResultReleaseMode.Manual, resultReleaseTime: releaseAt);
        Knows(exam);
        _queries.Revisions.Add(Revision(exam, Now.AddHours(-2), Now.AddMinutes(-20)));

        var summary = await _run.RunAsync(Now, CancellationToken.None);

        Assert.Equal(0, summary.RevisionNoticesSent);
        await _mailer.DidNotReceiveWithAnyArgs().SendScoreRevisedAsync(default!, default);
    }

    [Fact]
    public async Task ARevisionMoreThanADayOld_IsLeftAlone()
    {
        var exam = Fixtures.Exam([Fixtures.Question()], start: Now.AddDays(-4), end: Now.AddDays(-3));
        Knows(exam);
        _queries.Revisions.Add(Revision(exam, Now.AddDays(-3), Now.AddDays(-2)));

        var summary = await _run.RunAsync(Now, CancellationToken.None);

        Assert.Equal(0, summary.RevisionNoticesSent);
    }

    // ---- a run is its own ------------------------------------------------------------------------------

    [Fact]
    public async Task EachRunCountsOnlyItsOwnWork_AndReadsTheRosterAgain()
    {
        var exam = ExamStartingIn(TimeSpan.FromHours(10));
        var first = await _run.RunAsync(Now, CancellationToken.None);

        var second = await _run.RunAsync(Now.AddMinutes(5), CancellationToken.None);

        Assert.Equal(1, first.RemindersSent);
        Assert.Equal(0, second.RemindersSent);
        await _roster.Received(2).GetEnrolledCandidatesAsync(exam.Id, Arg.Any<CancellationToken>());
    }
}
