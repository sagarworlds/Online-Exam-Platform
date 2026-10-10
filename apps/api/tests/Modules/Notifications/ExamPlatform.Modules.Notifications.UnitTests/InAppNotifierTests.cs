using ExamPlatform.Modules.Notifications.Application;
using ExamPlatform.Modules.Notifications.Application.Exceptions;
using ExamPlatform.Modules.Notifications.Contracts;
using ExamPlatform.Modules.Notifications.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExamPlatform.Modules.Notifications.UnitTests;

/// <summary>What the in-app notifier records, and that it never records one notice twice (FR-39).</summary>
public class InAppNotifierTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);

    private readonly InMemoryNotices _store = new();
    private readonly FakeNotificationUnitOfWork _unitOfWork;
    private readonly InAppNotifier _notifier;

    public InAppNotifierTests()
    {
        _unitOfWork = new FakeNotificationUnitOfWork(_store);
        _notifier = new InAppNotifier(_store, _unitOfWork, new FixedClock(Now), NullLogger<InAppNotifier>.Instance);
    }

    [Fact]
    public async Task Notify_RecordsTheNoticeForTheAccount_AndSavesIt()
    {
        var account = Guid.NewGuid();
        var subject = Guid.NewGuid();

        var recorded = await _notifier.NotifyAsync(new InAppNotice(account, InAppNoticeKind.ResultReleased, subject, "Physics"), CancellationToken.None);

        Assert.True(recorded);
        var notice = Assert.Single(_store.Notices);
        Assert.Equal(account, notice.RecipientUserId);
        Assert.Equal(NoticeKind.ResultReleased, notice.Kind);
        Assert.Equal(subject, notice.SubjectId);
        Assert.Equal("Physics", notice.ExamName);
        Assert.Equal(Now, notice.CreatedAtUtc);
        Assert.Equal(1, _unitOfWork.Saves);
    }

    [Fact]
    public async Task Notify_TheSameNoticeTwice_RecordsItOnce()
    {
        var notice = new InAppNotice(Guid.NewGuid(), InAppNoticeKind.ExamReminder24Hours, Guid.NewGuid(), "Chemistry");

        await _notifier.NotifyAsync(notice, CancellationToken.None);
        var again = await _notifier.NotifyAsync(notice, CancellationToken.None);

        Assert.True(again);
        Assert.Single(_store.Notices);
        Assert.Equal(1, _unitOfWork.Saves);
    }

    [Fact]
    public async Task Notify_TheSameEventForAnotherKind_IsAFreshNotice()
    {
        // A 24-hour reminder and the hour reminder for one exam are two notices, as they are two e-mails.
        var account = Guid.NewGuid();
        var exam = Guid.NewGuid();

        await _notifier.NotifyAsync(new InAppNotice(account, InAppNoticeKind.ExamReminder24Hours, exam, "Maths"), CancellationToken.None);
        await _notifier.NotifyAsync(new InAppNotice(account, InAppNoticeKind.ExamReminderOneHour, exam, "Maths"), CancellationToken.None);

        Assert.Equal(2, _store.Notices.Count);
    }

    [Fact]
    public async Task NotifyMany_LeavesOutWhatIsAlreadyThereAndWhatRepeatsWithinTheBatch()
    {
        var exam = Guid.NewGuid();
        var alreadyTold = Guid.NewGuid();
        var newcomer = Guid.NewGuid();
        _store.Notices.Add(InAppNotification.Create(alreadyTold, NoticeKind.ExamReminder24Hours, exam, "Biology", Now));

        var batch = new List<InAppNotice>
        {
            new(alreadyTold, InAppNoticeKind.ExamReminder24Hours, exam, "Biology"),
            new(newcomer, InAppNoticeKind.ExamReminder24Hours, exam, "Biology"),
            new(newcomer, InAppNoticeKind.ExamReminder24Hours, exam, "Biology"),
        };

        var recorded = await _notifier.NotifyManyAsync(batch, CancellationToken.None);

        Assert.True(recorded);
        Assert.Equal(2, _store.Notices.Count);
        Assert.Contains(_store.Notices, n => n.RecipientUserId == newcomer);
        Assert.Equal(1, _unitOfWork.Saves);
    }

    [Fact]
    public async Task NotifyMany_WithNothingNew_SavesNothing()
    {
        var account = Guid.NewGuid();
        var exam = Guid.NewGuid();
        await _notifier.NotifyAsync(new InAppNotice(account, InAppNoticeKind.ResultReleased, exam, null), CancellationToken.None);

        var recorded = await _notifier.NotifyManyAsync(
            [new InAppNotice(account, InAppNoticeKind.ResultReleased, exam, null)], CancellationToken.None);

        Assert.True(recorded);
        Assert.Equal(1, _unitOfWork.Saves);
    }

    [Fact]
    public async Task Notify_WhenAConcurrentRequestRecordsTheSameNoticeFirst_ItIsStillInTheFeed()
    {
        var notice = new InAppNotice(Guid.NewGuid(), InAppNoticeKind.AttemptRequestApproved, Guid.NewGuid(), "Art");

        // The other request saves the notice between this one's check and its save, so this save is the one that fails.
        _unitOfWork.BeforeNextSave = () => _store.Notices.Add(InAppNotification.Create(
            notice.RecipientUserId, NoticeKind.AttemptRequestApproved, notice.SubjectId, notice.ExamName, Now));
        _unitOfWork.FailNextSaveWith = () => new NotificationAlreadyRecordedException(new InvalidOperationException("duplicate"));

        var recorded = await _notifier.NotifyAsync(notice, CancellationToken.None);

        Assert.True(recorded);
        Assert.Single(_store.Notices);
    }

    [Fact]
    public async Task Notify_WhenTheStoreFails_ReportsItAsNotRecorded_AndDoesNotThrow()
    {
        _unitOfWork.FailNextSaveWith = () => new NotificationStoreException("down", new InvalidOperationException("no connection"));

        var recorded = await _notifier.NotifyAsync(
            new InAppNotice(Guid.NewGuid(), InAppNoticeKind.DisputeRejected, Guid.NewGuid(), "Math"), CancellationToken.None);

        Assert.False(recorded);
        Assert.Empty(_store.Notices);
    }

    [Fact]
    public async Task NotifyMany_WhenTheStoreFailsAgainAfterARace_ReportsNotRecorded()
    {
        _unitOfWork.FailEverySaveWith = () => new NotificationAlreadyRecordedException(new InvalidOperationException("duplicate"));

        var recorded = await _notifier.NotifyManyAsync(
            [new InAppNotice(Guid.NewGuid(), InAppNoticeKind.ScoreRevised, Guid.NewGuid(), "Math")], CancellationToken.None);

        Assert.False(recorded);
        Assert.Empty(_store.Notices);
    }

    [Fact]
    public async Task NotifyMany_WithAnEmptyBatch_IsTriviallyDone()
    {
        Assert.True(await _notifier.NotifyManyAsync([], CancellationToken.None));
        Assert.Equal(0, _unitOfWork.Saves);
    }

    [Theory]
    [MemberData(nameof(EveryContractKind))]
    public void ToDomain_MapsEveryContractKindToTheModulesKindOfTheSameName(InAppNoticeKind kind)
    {
        // The two enums are kept equal by name; a kind added to one and not the other fails here, not in a feed that silently shows nothing.
        Assert.Equal(kind.ToString(), InAppNotifier.ToDomain(kind).ToString());
    }

    public static IEnumerable<object[]> EveryContractKind() =>
        Enum.GetValues<InAppNoticeKind>().Select(k => new object[] { k });
}
