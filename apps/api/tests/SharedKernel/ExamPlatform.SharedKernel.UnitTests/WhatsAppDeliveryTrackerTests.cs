using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Infrastructure.WhatsApp;

namespace ExamPlatform.SharedKernel.UnitTests;

/// <summary>What the webhook says became of recent messages, kept so the administrator's WhatsApp test can show it.</summary>
public class WhatsAppDeliveryTrackerTests
{
    private sealed class MutableClock(DateTime now) : Clock
    {
        public DateTime UtcNow { get; set; } = now;
    }

    private static readonly DateTime Start = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    private readonly MutableClock _clock = new(Start);

    private InMemoryWhatsAppDeliveryTracker Tracker() => new(_clock);

    private static WhatsAppDeliveryStatus Status(string id, string status, int? errorCode = null, string? errorTitle = null) =>
        new(id, status, "919876543210", errorCode, errorTitle);

    [Fact]
    public void AMessageNobodyHasReportedOn_IsUnknown()
    {
        Assert.Null(Tracker().Find("wamid.nothing"));
    }

    [Fact]
    public void AReport_IsKept_WithTheRecipientMasked_AndWhenItArrived()
    {
        var tracker = Tracker();

        tracker.Record(Status("wamid.1", "sent"));

        var report = tracker.Find("wamid.1")!;
        Assert.Equal("sent", report.Status);
        Assert.Equal("**********10", report.MaskedRecipient);
        Assert.Equal(Start, report.UpdatedAtUtc);
        Assert.Null(report.Failure);
        // The full number is never kept.
        Assert.DoesNotContain("919876543210", report.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ProgressIsKept_AndALateEarlierReportCannotUndoIt()
    {
        var tracker = Tracker();

        tracker.Record(Status("wamid.1", "sent"));
        tracker.Record(Status("wamid.1", "delivered"));
        tracker.Record(Status("wamid.1", "read"));
        // Reports can arrive out of order.
        tracker.Record(Status("wamid.1", "sent"));
        tracker.Record(Status("wamid.1", "delivered"));

        Assert.Equal("read", tracker.Find("wamid.1")!.Status);
    }

    [Fact]
    public void AFailure_ReplacesProgress_AndCarriesAnExplanationOfMetasReason()
    {
        var tracker = Tracker();
        tracker.Record(Status("wamid.1", "sent"));

        tracker.Record(Status("wamid.1", "failed", 131026, "Message undeliverable"));

        var report = tracker.Find("wamid.1")!;
        Assert.Equal("failed", report.Status);
        var failure = Assert.IsType<WhatsAppFailure>(report.Failure);
        Assert.Equal(WhatsAppFailureKind.NotOnWhatsApp, failure.Kind);
        Assert.Equal(131026, failure.MetaCode);
        Assert.Equal("Message undeliverable", failure.MetaMessage);
    }

    [Fact]
    public void AFailure_WithNoCode_IsStillReportedAsFailed()
    {
        var tracker = Tracker();

        tracker.Record(Status("wamid.1", "failed"));

        Assert.NotNull(tracker.Find("wamid.1")!.Failure);
    }

    [Theory]
    [InlineData("deleted")]
    [InlineData("warning")]
    [InlineData("")]
    public void AStatusThePlatformHasNoUseFor_IsIgnored(string status)
    {
        var tracker = Tracker();

        tracker.Record(Status("wamid.1", status));

        Assert.Null(tracker.Find("wamid.1"));
    }

    [Fact]
    public void AReportWithoutAMessageId_IsIgnored()
    {
        var tracker = Tracker();

        tracker.Record(Status("", "sent"));

        Assert.Null(tracker.Find(""));
    }

    [Fact]
    public void AReport_IsForgottenAfterADay()
    {
        var tracker = Tracker();
        tracker.Record(Status("wamid.1", "delivered"));

        _clock.UtcNow = Start + InMemoryWhatsAppDeliveryTracker.Retention - TimeSpan.FromSeconds(1);
        Assert.NotNull(tracker.Find("wamid.1"));

        _clock.UtcNow = Start + InMemoryWhatsAppDeliveryTracker.Retention;
        Assert.Null(tracker.Find("wamid.1"));
    }

    [Fact]
    public void OnlyTheMostRecentMessagesAreRemembered_TheOldestGoesFirst()
    {
        var tracker = Tracker();

        for (var i = 0; i < InMemoryWhatsAppDeliveryTracker.Capacity + 1; i++)
        {
            tracker.Record(Status($"wamid.{i}", "sent"));
        }

        Assert.Null(tracker.Find("wamid.0"));
        Assert.NotNull(tracker.Find("wamid.1"));
        Assert.NotNull(tracker.Find($"wamid.{InMemoryWhatsAppDeliveryTracker.Capacity}"));
    }

    [Fact]
    public void UpdatingAMessageAlreadyKnown_DoesNotCostAnotherMessageItsPlace()
    {
        var tracker = Tracker();
        for (var i = 0; i < InMemoryWhatsAppDeliveryTracker.Capacity; i++)
        {
            tracker.Record(Status($"wamid.{i}", "sent"));
        }

        tracker.Record(Status("wamid.0", "delivered"));
        tracker.Record(Status("wamid.0", "read"));

        Assert.Equal("read", tracker.Find("wamid.0")!.Status);
        Assert.NotNull(tracker.Find("wamid.1"));
    }

    [Fact]
    public async Task ManyReportsAtOnce_AreSafe()
    {
        var tracker = Tracker();

        await Task.WhenAll(Enumerable.Range(0, 200).Select(i => Task.Run(() =>
        {
            tracker.Record(Status($"wamid.{i % 20}", "sent"));
            tracker.Record(Status($"wamid.{i % 20}", "delivered"));
            tracker.Find($"wamid.{i % 20}");
        })));

        Assert.All(Enumerable.Range(0, 20), i => Assert.Equal("delivered", tracker.Find($"wamid.{i}")!.Status));
    }
}
