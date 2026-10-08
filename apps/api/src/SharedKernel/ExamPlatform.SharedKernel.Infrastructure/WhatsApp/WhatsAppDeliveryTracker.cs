using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.SharedKernel.Infrastructure.WhatsApp;

/// <summary>What Meta last reported about one message: how far it got, and why not further if it failed.</summary>
/// <param name="MessageId">WhatsApp's id for the message.</param>
/// <param name="Status">The furthest status reported: <c>sent</c>, <c>delivered</c>, <c>read</c> or <c>failed</c>.</param>
/// <param name="MaskedRecipient">The recipient, masked to its last two digits.</param>
/// <param name="Failure">Why it failed, when <paramref name="Status"/> is <c>failed</c>.</param>
/// <param name="UpdatedAtUtc">When the report arrived.</param>
public sealed record WhatsAppDeliveryReport(
    string MessageId, string Status, string? MaskedRecipient, WhatsAppFailure? Failure, DateTime UpdatedAtUtc);

/// <summary>
/// Remembers what the webhook says became of recent messages, so the administrator's WhatsApp test can show "delivered" or the real
/// reason a message that WhatsApp accepted never arrived (a number that is not on WhatsApp is only ever reported this way).
/// </summary>
public interface IWhatsAppDeliveryTracker
{
    /// <summary>Notes a delivery report from the webhook.</summary>
    /// <param name="status">The report.</param>
    void Record(WhatsAppDeliveryStatus status);

    /// <summary>The latest report for a message, or <see langword="null"/> when none has arrived (or it is too old to keep).</summary>
    /// <param name="messageId">WhatsApp's id for the message.</param>
    WhatsAppDeliveryReport? Find(string messageId);
}

/// <summary>
/// An <see cref="IWhatsAppDeliveryTracker"/> kept in memory: the last <see cref="Capacity"/> messages for <see cref="Retention"/>. It is a
/// diagnostic aid, not a record, so it is lost on a restart and not shared between instances; the webhook's log lines remain the durable
/// trail.
/// </summary>
public sealed class InMemoryWhatsAppDeliveryTracker(Clock clock) : IWhatsAppDeliveryTracker
{
    /// <summary>How many messages are remembered; the oldest is forgotten first.</summary>
    public const int Capacity = 500;

    /// <summary>How long a report is kept.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    private readonly object _gate = new();
    private readonly Dictionary<string, WhatsAppDeliveryReport> _reports = [];
    private readonly Queue<string> _order = new();

    /// <inheritdoc />
    public void Record(WhatsAppDeliveryStatus status)
    {
        var rank = Rank(status.Status);
        if (rank == 0 || string.IsNullOrEmpty(status.MessageId))
        {
            return; // A status this platform has no use for (deleted, warning ...).
        }

        var nowUtc = clock.UtcNow;
        lock (_gate)
        {
            if (_reports.TryGetValue(status.MessageId, out var known))
            {
                // Reports can arrive out of order: a late "sent" must not undo "delivered", but a failure is final news.
                if (Rank(known.Status) >= rank)
                {
                    return;
                }
            }
            else
            {
                _order.Enqueue(status.MessageId);
                while (_order.Count > Capacity)
                {
                    _reports.Remove(_order.Dequeue());
                }
            }

            _reports[status.MessageId] = new WhatsAppDeliveryReport(
                status.MessageId,
                status.Status,
                status.Recipient is null ? null : WhatsAppPhoneNumber.Mask(status.Recipient),
                status.Status == "failed"
                    ? WhatsAppErrorGuide.Explain(null, status.ErrorCode, WhatsAppErrorGuide.Describe(status.ErrorTitle, status.ErrorDetails, null))
                    : null,
                nowUtc);
        }
    }

    /// <inheritdoc />
    public WhatsAppDeliveryReport? Find(string messageId)
    {
        lock (_gate)
        {
            return _reports.TryGetValue(messageId, out var report) && clock.UtcNow - report.UpdatedAtUtc < Retention ? report : null;
        }
    }

    // How far along a message is; a higher rank replaces a lower one.
    private static int Rank(string status) => status switch
    {
        "sent" => 1,
        "delivered" => 2,
        "read" => 3,
        "failed" => 4,
        _ => 0,
    };
}
