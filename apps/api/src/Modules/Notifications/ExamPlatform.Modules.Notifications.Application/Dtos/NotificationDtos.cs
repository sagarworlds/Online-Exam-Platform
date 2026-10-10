using ExamPlatform.Modules.Notifications.Domain;

namespace ExamPlatform.Modules.Notifications.Application.Dtos;

/// <summary>One notice as its recipient sees it.</summary>
/// <param name="Id">The notice's id.</param>
/// <param name="Kind">The event it is about; the feed words it from this.</param>
/// <param name="SubjectId">The thing the event happened to; a link target for the feed, if it has one.</param>
/// <param name="ExamName">The exam's name at the time, or null when the notice is not about an exam.</param>
/// <param name="CreatedAtUtc">When it was recorded.</param>
/// <param name="ReadAtUtc">When the recipient first read it, or null.</param>
/// <param name="IsRead">Whether the recipient has read it.</param>
public sealed record NotificationDto(
    Guid Id,
    NoticeKind Kind,
    Guid SubjectId,
    string? ExamName,
    DateTime CreatedAtUtc,
    DateTime? ReadAtUtc,
    bool IsRead);

/// <summary>One page of the feed, with the counts the page is shown with.</summary>
/// <param name="Items">The notices on this page, unread first.</param>
/// <param name="Page">The page, from 1.</param>
/// <param name="PageSize">How many notices a page holds at most.</param>
/// <param name="TotalCount">How many notices the account has in all.</param>
/// <param name="UnreadCount">How many of them are unread.</param>
public sealed record NotificationPageDto(IReadOnlyList<NotificationDto> Items, int Page, int PageSize, int TotalCount, int UnreadCount);

/// <summary>Turns a notice into the DTO the feed returns.</summary>
public static class NotificationMapping
{
    /// <summary>The DTO for a notice.</summary>
    /// <param name="notification">The notice.</param>
    public static NotificationDto ToDto(this InAppNotification notification) => new(
        notification.Id,
        notification.Kind,
        notification.SubjectId,
        notification.ExamName,
        notification.CreatedAtUtc,
        notification.ReadAtUtc,
        notification.IsRead);
}
