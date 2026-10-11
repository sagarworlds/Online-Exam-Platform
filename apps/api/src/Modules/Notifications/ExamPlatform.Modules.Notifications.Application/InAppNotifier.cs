using ExamPlatform.Modules.Notifications.Application.Exceptions;
using ExamPlatform.Modules.Notifications.Application.Ports;
using ExamPlatform.Modules.Notifications.Contracts;
using ExamPlatform.Modules.Notifications.Domain;
using ExamPlatform.SharedKernel.Application;
using Microsoft.Extensions.Logging;

namespace ExamPlatform.Modules.Notifications.Application;

/// <summary>
/// The <see cref="IInAppNotifier"/> the other modules use. It records a notice once: an event announced again finds the notice already there
/// and adds nothing (FR-39). A store failure is logged with the notices' details and reported as <c>false</c>, never thrown, because the
/// change that raised the notice is already saved and must not be reported as failed on account of the feed.
/// </summary>
/// <param name="notifications">Where notices are kept.</param>
/// <param name="unitOfWork">Saves them.</param>
/// <param name="clock">The time a notice is recorded at.</param>
/// <param name="logger">Where a notice that could not be recorded is logged.</param>
public sealed class InAppNotifier(
    IInAppNotificationRepository notifications,
    IInAppNotificationUnitOfWork unitOfWork,
    Clock clock,
    ILogger<InAppNotifier> logger) : IInAppNotifier
{
    /// <inheritdoc />
    public Task<bool> NotifyAsync(InAppNotice notice, CancellationToken cancellationToken) =>
        NotifyManyAsync([notice], cancellationToken);

    /// <inheritdoc />
    public async Task<bool> NotifyManyAsync(IReadOnlyCollection<InAppNotice> notices, CancellationToken cancellationToken)
    {
        if (notices.Count == 0)
            return true;

        try
        {
            return await RecordNewAsync(notices, cancellationToken) || await RetryAfterConcurrentWriteAsync(notices, cancellationToken);
        }
        catch (NotificationStoreException exception)
        {
            LogNotRecorded(exception, notices);
            return false;
        }
    }

    /// <summary>Maps the kind other modules name to the module's own kind. The two lists are kept equal by the unit tests.</summary>
    /// <param name="kind">The kind as a caller names it.</param>
    /// <returns>The module's kind.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The kind is not one this module knows.</exception>
    public static NoticeKind ToDomain(InAppNoticeKind kind) => kind switch
    {
        InAppNoticeKind.InviteReceived => NoticeKind.InviteReceived,
        InAppNoticeKind.AttemptRequestReceived => NoticeKind.AttemptRequestReceived,
        InAppNoticeKind.AttemptRequestApproved => NoticeKind.AttemptRequestApproved,
        InAppNoticeKind.AttemptRequestDeclined => NoticeKind.AttemptRequestDeclined,
        InAppNoticeKind.DisputeRejected => NoticeKind.DisputeRejected,
        InAppNoticeKind.DisputeAccepted => NoticeKind.DisputeAccepted,
        InAppNoticeKind.ExamReminder24Hours => NoticeKind.ExamReminder24Hours,
        InAppNoticeKind.ExamReminderOneHour => NoticeKind.ExamReminderOneHour,
        InAppNoticeKind.ResultReleased => NoticeKind.ResultReleased,
        InAppNoticeKind.ScoreRevised => NoticeKind.ScoreRevised,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "This notice kind has no feed entry."),
    };

    // Returns true when the batch is in the feed after one attempt. A concurrent request recording one of the same notices in the gap between
    // the check and the save makes the save fail; that one attempt is not a failure, so the caller tries once more and the check then leaves
    // out what the other request wrote.
    private async Task<bool> RecordNewAsync(IReadOnlyCollection<InAppNotice> notices, CancellationToken cancellationToken)
    {
        try
        {
            await SaveNewAsync(notices, cancellationToken);
            return true;
        }
        catch (NotificationAlreadyRecordedException)
        {
            return false;
        }
    }

    private async Task<bool> RetryAfterConcurrentWriteAsync(IReadOnlyCollection<InAppNotice> notices, CancellationToken cancellationToken)
    {
        try
        {
            await SaveNewAsync(notices, cancellationToken);
            return true;
        }
        catch (NotificationAlreadyRecordedException exception)
        {
            LogNotRecorded(exception, notices);
            return false;
        }
    }

    // Adds the notices that are not in the feed yet and saves them in one go. Nothing is added when all of them are there already.
    private async Task SaveNewAsync(IReadOnlyCollection<InAppNotice> notices, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var added = 0;

        foreach (var group in notices.GroupBy(n => n.Kind))
        {
            var kind = ToDomain(group.Key);
            var subjects = group.Select(n => n.SubjectId).Distinct().ToList();
            var recorded = (await notifications.ListRecordedAsync(kind, subjects, cancellationToken)).ToHashSet();

            // Within the batch a repeated notice is recorded once, so the store's unique index is never asked to take it twice.
            foreach (var notice in group.DistinctBy(n => (n.RecipientUserId, n.SubjectId)))
            {
                if (recorded.Contains((notice.RecipientUserId, notice.SubjectId)))
                    continue;

                notifications.Add(InAppNotification.Create(notice.RecipientUserId, kind, notice.SubjectId, notice.ExamName, now));
                added++;
            }
        }

        if (added > 0)
            await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private void LogNotRecorded(Exception exception, IReadOnlyCollection<InAppNotice> notices)
    {
        // The batch is logged as a count and the first notice's details: enough to find the cause, without writing every recipient to the log.
        var first = notices.First();
        logger.LogError(
            exception,
            "{Count} in-app notice(s) could not be recorded, starting with kind {Kind} about {SubjectId}; the changes they announce were saved.",
            notices.Count,
            first.Kind,
            first.SubjectId);
    }
}
