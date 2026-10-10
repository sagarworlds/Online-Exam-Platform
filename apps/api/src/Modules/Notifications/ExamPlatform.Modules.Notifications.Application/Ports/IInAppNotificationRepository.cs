using ExamPlatform.Modules.Notifications.Domain;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.Notifications.Application.Ports;

/// <summary>Persistence port for <see cref="InAppNotification"/>. Every read is limited to one recipient, so an account only ever sees its own feed.</summary>
public interface IInAppNotificationRepository
{
    /// <summary>Whether the account already has this notice recorded.</summary>
    /// <param name="recipientUserId">The account.</param>
    /// <param name="kind">The event.</param>
    /// <param name="subjectId">The thing the event happened to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> ExistsAsync(Guid recipientUserId, NoticeKind kind, Guid subjectId, CancellationToken cancellationToken);

    /// <summary>Which of the given subjects already have a notice of this kind, for each recipient who has one.</summary>
    /// <param name="kind">The event.</param>
    /// <param name="subjectIds">The things the event happened to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The (recipient, subject) pairs that are already recorded.</returns>
    Task<IReadOnlyList<(Guid RecipientUserId, Guid SubjectId)>> ListRecordedAsync(
        NoticeKind kind, IReadOnlyCollection<Guid> subjectIds, CancellationToken cancellationToken);

    /// <summary>Starts tracking a new notice; it is stored when the unit of work saves.</summary>
    /// <param name="notification">The notice to add.</param>
    void Add(InAppNotification notification);

    /// <summary>Loads one of the account's notices, tracked so that marking it read is saved.</summary>
    /// <param name="id">The notice.</param>
    /// <param name="recipientUserId">The account; a notice of anyone else is not found.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The notice, or null when the account has no such notice.</returns>
    Task<InAppNotification?> GetForRecipientAsync(Guid id, Guid recipientUserId, CancellationToken cancellationToken);

    /// <summary>Loads every unread notice of the account, tracked so that marking them read is saved.</summary>
    /// <param name="recipientUserId">The account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<InAppNotification>> ListUnreadForRecipientAsync(Guid recipientUserId, CancellationToken cancellationToken);

    /// <summary>One page of the account's notices, unread first and then newest first, so what needs attention is on the first page.</summary>
    /// <param name="recipientUserId">The account.</param>
    /// <param name="page">Which page, and how big.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<InAppNotification>> ListPageForRecipientAsync(Guid recipientUserId, PageRequest page, CancellationToken cancellationToken);

    /// <summary>How many notices the account has in all.</summary>
    /// <param name="recipientUserId">The account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<int> CountForRecipientAsync(Guid recipientUserId, CancellationToken cancellationToken);

    /// <summary>How many of the account's notices are unread.</summary>
    /// <param name="recipientUserId">The account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<int> CountUnreadForRecipientAsync(Guid recipientUserId, CancellationToken cancellationToken);
}
