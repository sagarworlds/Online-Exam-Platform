using ExamPlatform.Modules.Notifications.Application.Dtos;
using ExamPlatform.Modules.Notifications.Application.Ports;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.Notifications.Application.Queries;

/// <summary>Lists the signed-in account's own feed (FR-39), a page at a time.</summary>
/// <param name="notifications">Where the notices are kept.</param>
public sealed class ListMyNotificationsHandler(IInAppNotificationRepository notifications)
{
    /// <summary>Returns one page of the account's notices, unread first, with the totals the feed shows beside it.</summary>
    /// <param name="recipientUserId">The signed-in account, from the token.</param>
    /// <param name="page">The page, from 1; the first when null.</param>
    /// <param name="pageSize">How many notices a page holds; the default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ExamPlatform.SharedKernel.Domain.Exceptions.InvalidPageRequestError">The page or the page size is out of range.</exception>
    public async Task<NotificationPageDto> HandleAsync(Guid recipientUserId, int? page, int? pageSize, CancellationToken cancellationToken)
    {
        var request = PageRequest.Create(page, pageSize);
        var items = await notifications.ListPageForRecipientAsync(recipientUserId, request, cancellationToken);
        var total = await notifications.CountForRecipientAsync(recipientUserId, cancellationToken);
        var unread = await notifications.CountUnreadForRecipientAsync(recipientUserId, cancellationToken);

        return new NotificationPageDto(items.Select(n => n.ToDto()).ToList(), request.Page, request.PageSize, total, unread);
    }
}

/// <summary>Counts the notices the signed-in account has not read, for the badge the web shows.</summary>
/// <param name="notifications">Where the notices are kept.</param>
public sealed class CountUnreadNotificationsHandler(IInAppNotificationRepository notifications)
{
    /// <summary>How many of the account's notices are unread.</summary>
    /// <param name="recipientUserId">The signed-in account, from the token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<int> HandleAsync(Guid recipientUserId, CancellationToken cancellationToken) =>
        notifications.CountUnreadForRecipientAsync(recipientUserId, cancellationToken);
}
