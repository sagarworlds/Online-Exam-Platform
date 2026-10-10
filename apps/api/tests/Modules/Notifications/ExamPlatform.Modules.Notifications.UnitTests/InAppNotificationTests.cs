using ExamPlatform.Modules.Notifications.Domain;

namespace ExamPlatform.Modules.Notifications.UnitTests;

/// <summary>The feed entry itself: what it keeps, and when it counts as read (FR-39).</summary>
public class InAppNotificationTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_IsUnreadAndKeepsWhoWhatAndWhen()
    {
        var recipient = Guid.NewGuid();
        var subject = Guid.NewGuid();

        var notice = InAppNotification.Create(recipient, NoticeKind.ResultReleased, subject, "Physics", Now);

        Assert.NotEqual(Guid.Empty, notice.Id);
        Assert.Equal(recipient, notice.RecipientUserId);
        Assert.Equal(NoticeKind.ResultReleased, notice.Kind);
        Assert.Equal(subject, notice.SubjectId);
        Assert.Equal("Physics", notice.ExamName);
        Assert.Equal(Now, notice.CreatedAtUtc);
        Assert.False(notice.IsRead);
        Assert.Null(notice.ReadAtUtc);
    }

    [Fact]
    public void Create_RefusesAnEmptyRecipient()
    {
        Assert.Throws<ArgumentException>(() => InAppNotification.Create(Guid.Empty, NoticeKind.InviteReceived, Guid.NewGuid(), null, Now));
    }

    [Fact]
    public void Create_RefusesAnEmptySubject()
    {
        Assert.Throws<ArgumentException>(() => InAppNotification.Create(Guid.NewGuid(), NoticeKind.InviteReceived, Guid.Empty, null, Now));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("  Physics  ", "Physics")]
    public void Create_TrimsTheExamName_AndStoresBlankAsNone(string? given, string? expected)
    {
        var notice = InAppNotification.Create(Guid.NewGuid(), NoticeKind.ExamReminderOneHour, Guid.NewGuid(), given, Now);

        Assert.Equal(expected, notice.ExamName);
    }

    [Fact]
    public void Create_CutsAnExamNameTooLongForTheColumn_RatherThanRefusingTheNotice()
    {
        var longName = new string('x', InAppNotification.MaxExamNameLength + 40);

        var notice = InAppNotification.Create(Guid.NewGuid(), NoticeKind.ScoreRevised, Guid.NewGuid(), longName, Now);

        Assert.Equal(InAppNotification.MaxExamNameLength, notice.ExamName!.Length);
    }

    [Fact]
    public void MarkRead_SetsTheReadTime_AndASecondReadKeepsTheFirst()
    {
        var notice = InAppNotification.Create(Guid.NewGuid(), NoticeKind.DisputeRejected, Guid.NewGuid(), null, Now);

        notice.MarkRead(Now.AddMinutes(5));
        notice.MarkRead(Now.AddMinutes(90));

        Assert.True(notice.IsRead);
        Assert.Equal(Now.AddMinutes(5), notice.ReadAtUtc);
    }
}
