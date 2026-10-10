using ExamPlatform.Modules.Notifications.Application.Dtos;
using ExamPlatform.Modules.Notifications.Application.Ports;
using ExamPlatform.Modules.Notifications.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.Notifications.Application.Commands;

/// <summary>Marks one of the signed-in account's notices as read.</summary>
/// <param name="notifications">Where the notices are kept.</param>
/// <param name="unitOfWork">Saves the change.</param>
/// <param name="clock">The time it was read.</param>
public sealed class MarkNotificationReadHandler(
    IInAppNotificationRepository notifications, IInAppNotificationUnitOfWork unitOfWork, Clock clock)
{
    /// <summary>Marks the notice read. Reading one that is already read changes nothing and is not an error.</summary>
    /// <param name="notificationId">The notice.</param>
    /// <param name="recipientUserId">The signed-in account, from the token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The notice as it now is.</returns>
    /// <exception cref="NotificationNotFoundError">The account has no notice with that id (another account's notice answers the same way).</exception>
    public async Task<NotificationDto> HandleAsync(Guid notificationId, Guid recipientUserId, CancellationToken cancellationToken)
    {
        var notification = await notifications.GetForRecipientAsync(notificationId, recipientUserId, cancellationToken)
            ?? throw new NotificationNotFoundError();

        notification.MarkRead(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return notification.ToDto();
    }
}

/// <summary>Marks every unread notice of the signed-in account as read.</summary>
/// <param name="notifications">Where the notices are kept.</param>
/// <param name="unitOfWork">Saves the change.</param>
/// <param name="clock">The time they were read.</param>
public sealed class MarkAllNotificationsReadHandler(
    IInAppNotificationRepository notifications, IInAppNotificationUnitOfWork unitOfWork, Clock clock)
{
    /// <summary>Marks the account's unread notices read, in one save.</summary>
    /// <param name="recipientUserId">The signed-in account, from the token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many notices were unread and are now read.</returns>
    public async Task<int> HandleAsync(Guid recipientUserId, CancellationToken cancellationToken)
    {
        var unread = await notifications.ListUnreadForRecipientAsync(recipientUserId, cancellationToken);
        if (unread.Count == 0)
            return 0;

        var now = clock.UtcNow;
        foreach (var notification in unread)
            notification.MarkRead(now);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return unread.Count;
    }
}
