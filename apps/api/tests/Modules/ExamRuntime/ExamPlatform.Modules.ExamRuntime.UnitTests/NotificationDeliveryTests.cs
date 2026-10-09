using ExamPlatform.Modules.ExamRuntime.Domain;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>The record that a scheduled e-mail was sent, or tried and failed (FR-39): sent once, retried a while apart, then given up on.</summary>
public class NotificationDeliveryTests
{
    private readonly Guid _subject = Guid.NewGuid();
    private readonly Guid _candidate = Guid.NewGuid();

    private NotificationDelivery Fresh() => NotificationDelivery.Start(NotificationKind.ExamReminder24Hours, _subject, _candidate);

    [Fact]
    public void ANewRecord_IsUntried_Due_AndNotSettled()
    {
        var delivery = Fresh();

        Assert.Equal(NotificationKind.ExamReminder24Hours, delivery.Kind);
        Assert.Equal(_subject, delivery.SubjectId);
        Assert.Equal(_candidate, delivery.RecipientId);
        Assert.Equal(0, delivery.Attempts);
        Assert.Null(delivery.SentAtUtc);
        Assert.False(delivery.IsSettled);
        Assert.True(delivery.IsDueAt(Fixtures.Now));
    }

    [Fact]
    public void ASentMessage_IsSettled_AndNeverDueAgain()
    {
        var delivery = Fresh();

        delivery.RecordAttempt(sent: true, Fixtures.Now);

        Assert.Equal(Fixtures.Now, delivery.SentAtUtc);
        Assert.Equal(1, delivery.Attempts);
        Assert.True(delivery.IsSettled);
        Assert.False(delivery.IsDueAt(Fixtures.Now.AddDays(10)));
    }

    [Fact]
    public void AFailedTry_IsNotDueAgainUntilTheRetryDelayHasPassed()
    {
        var delivery = Fresh();

        delivery.RecordAttempt(sent: false, Fixtures.Now);

        Assert.Null(delivery.SentAtUtc);
        Assert.False(delivery.IsSettled);
        Assert.False(delivery.IsDueAt(Fixtures.Now.AddMinutes(14)));
        Assert.True(delivery.IsDueAt(Fixtures.Now + NotificationDelivery.RetryAfter));
    }

    [Fact]
    public void AMessageThatKeepsFailing_IsGivenUpOn_AfterTheMostTries()
    {
        var delivery = Fresh();
        var now = Fixtures.Now;

        for (var i = 0; i < NotificationDelivery.MaxAttempts; i++)
        {
            Assert.True(delivery.IsDueAt(now));
            delivery.RecordAttempt(sent: false, now);
            now += NotificationDelivery.RetryAfter;
        }

        Assert.Equal(NotificationDelivery.MaxAttempts, delivery.Attempts);
        Assert.Null(delivery.SentAtUtc);
        Assert.True(delivery.IsSettled);
        Assert.False(delivery.IsDueAt(now.AddYears(1)));
    }

    [Fact]
    public void ARetryThatWorks_SettlesTheMessage()
    {
        var delivery = Fresh();
        delivery.RecordAttempt(sent: false, Fixtures.Now);

        delivery.RecordAttempt(sent: true, Fixtures.Now.AddMinutes(20));

        Assert.Equal(2, delivery.Attempts);
        Assert.Equal(Fixtures.Now.AddMinutes(20), delivery.SentAtUtc);
        Assert.True(delivery.IsSettled);
    }
}
