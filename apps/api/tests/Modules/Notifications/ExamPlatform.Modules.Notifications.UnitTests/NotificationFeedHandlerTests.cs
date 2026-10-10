using ExamPlatform.Modules.Notifications.Application.Commands;
using ExamPlatform.Modules.Notifications.Application.Queries;
using ExamPlatform.Modules.Notifications.Domain;
using ExamPlatform.Modules.Notifications.Domain.Exceptions;
using ExamPlatform.SharedKernel.Domain.Exceptions;

namespace ExamPlatform.Modules.Notifications.UnitTests;

/// <summary>The feed a signed-in account reads and marks: only its own notices, unread first, and never another account's (FR-39).</summary>
public class NotificationFeedHandlerTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);

    private readonly InMemoryNotices _store = new();
    private readonly FakeNotificationUnitOfWork _unitOfWork;
    private readonly FixedClock _clock = new(Now);
    private readonly Guid _me = Guid.NewGuid();

    public NotificationFeedHandlerTests()
    {
        _unitOfWork = new FakeNotificationUnitOfWork(_store);
    }

    private InAppNotification Notice(Guid? recipient = null, DateTime? createdAtUtc = null, bool read = false)
    {
        var notice = InAppNotification.Create(recipient ?? _me, NoticeKind.ExamReminderOneHour, Guid.NewGuid(), "Physics", createdAtUtc ?? Now);
        if (read)
            notice.MarkRead(Now);

        _store.Notices.Add(notice);
        return notice;
    }

    [Fact]
    public async Task List_ShowsOnlyTheAccountsOwnNotices_WithTheTotalsBesideThem()
    {
        Notice(createdAtUtc: Now.AddMinutes(-1));
        Notice(createdAtUtc: Now.AddMinutes(-2), read: true);
        Notice(recipient: Guid.NewGuid());

        var page = await new ListMyNotificationsHandler(_store).HandleAsync(_me, null, null, CancellationToken.None);

        Assert.Equal(2, page.Items.Count);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(1, page.UnreadCount);
        Assert.Equal(1, page.Page);
        Assert.All(page.Items, item => Assert.Equal("ExamReminderOneHour", item.Kind.ToString()));
    }

    [Fact]
    public async Task List_PutsUnreadNoticesBeforeReadOnes_EvenWhenTheReadOnesAreNewer()
    {
        var newerRead = Notice(createdAtUtc: Now.AddMinutes(-1), read: true);
        var olderUnread = Notice(createdAtUtc: Now.AddMinutes(-30));

        var page = await new ListMyNotificationsHandler(_store).HandleAsync(_me, null, null, CancellationToken.None);

        Assert.Equal(olderUnread.Id, page.Items[0].Id);
        Assert.Equal(newerRead.Id, page.Items[1].Id);
    }

    [Fact]
    public async Task List_RefusesAPageOutsideTheRange()
    {
        await Assert.ThrowsAsync<InvalidPageRequestError>(
            () => new ListMyNotificationsHandler(_store).HandleAsync(_me, 0, null, CancellationToken.None));
    }

    [Fact]
    public async Task MarkRead_MarksTheAccountsOwnNotice_AndAnotherAccountsIsNotFound()
    {
        var mine = Notice();
        var theirs = Notice(recipient: Guid.NewGuid());
        var handler = new MarkNotificationReadHandler(_store, _unitOfWork, _clock);

        var dto = await handler.HandleAsync(mine.Id, _me, CancellationToken.None);

        Assert.True(dto.IsRead);
        Assert.Equal(Now, dto.ReadAtUtc);
        await Assert.ThrowsAsync<NotificationNotFoundError>(() => handler.HandleAsync(theirs.Id, _me, CancellationToken.None));
        Assert.False(theirs.IsRead);
    }

    [Fact]
    public async Task MarkAllRead_MarksEveryUnreadNoticeOfTheAccount_AndCountsThem()
    {
        Notice();
        Notice();
        Notice(read: true);
        var theirs = Notice(recipient: Guid.NewGuid());

        var marked = await new MarkAllNotificationsReadHandler(_store, _unitOfWork, _clock).HandleAsync(_me, CancellationToken.None);

        Assert.Equal(2, marked);
        Assert.Equal(0, await new CountUnreadNotificationsHandler(_store).HandleAsync(_me, CancellationToken.None));
        Assert.False(theirs.IsRead);
    }

    [Fact]
    public async Task MarkAllRead_WithNothingUnread_MarksNothingAndSavesNothing()
    {
        Notice(read: true);

        var marked = await new MarkAllNotificationsReadHandler(_store, _unitOfWork, _clock).HandleAsync(_me, CancellationToken.None);

        Assert.Equal(0, marked);
        Assert.Equal(0, _unitOfWork.Saves);
    }
}
