using ExamPlatform.Modules.Notifications.Application.Ports;
using ExamPlatform.Modules.Notifications.Domain;
using ExamPlatform.SharedKernel.Application;
using Microsoft.EntityFrameworkCore;

namespace ExamPlatform.Modules.Notifications.Infrastructure.Repositories;

/// <summary>EF Core-backed <see cref="IInAppNotificationRepository"/>. Every query names the recipient, so one account's feed is never read for another.</summary>
public sealed class InAppNotificationRepository(NotificationsDbContext context) : IInAppNotificationRepository
{
    /// <inheritdoc />
    public Task<bool> ExistsAsync(Guid recipientUserId, NoticeKind kind, Guid subjectId, CancellationToken cancellationToken) =>
        context.InAppNotifications.AsNoTracking().AnyAsync(
            n => n.RecipientUserId == recipientUserId && n.Kind == kind && n.SubjectId == subjectId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<(Guid RecipientUserId, Guid SubjectId)>> ListRecordedAsync(
        NoticeKind kind, IReadOnlyCollection<Guid> subjectIds, CancellationToken cancellationToken)
    {
        // The projection is anonymous because EF cannot translate a tuple; the tuples are built once the rows are in memory.
        var rows = await context.InAppNotifications.AsNoTracking()
            .Where(n => n.Kind == kind && subjectIds.Contains(n.SubjectId))
            .Select(n => new { n.RecipientUserId, n.SubjectId })
            .ToListAsync(cancellationToken);

        return rows.Select(r => (r.RecipientUserId, r.SubjectId)).ToList();
    }

    /// <inheritdoc />
    public void Add(InAppNotification notification) => context.InAppNotifications.Add(notification);

    /// <inheritdoc />
    public Task<InAppNotification?> GetForRecipientAsync(Guid id, Guid recipientUserId, CancellationToken cancellationToken) =>
        context.InAppNotifications.FirstOrDefaultAsync(n => n.Id == id && n.RecipientUserId == recipientUserId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<InAppNotification>> ListUnreadForRecipientAsync(Guid recipientUserId, CancellationToken cancellationToken) =>
        await context.InAppNotifications
            .Where(n => n.RecipientUserId == recipientUserId && n.ReadAtUtc == null)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<InAppNotification>> ListPageForRecipientAsync(
        Guid recipientUserId, PageRequest page, CancellationToken cancellationToken) =>
        await context.InAppNotifications.AsNoTracking()
            .Where(n => n.RecipientUserId == recipientUserId)
            // Unread (no read time) sorts before read; newest first within each group. The id breaks ties so pages never repeat a row.
            .OrderBy(n => n.ReadAtUtc == null ? 0 : 1)
            .ThenByDescending(n => n.CreatedAtUtc)
            .ThenBy(n => n.Id)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<int> CountForRecipientAsync(Guid recipientUserId, CancellationToken cancellationToken) =>
        context.InAppNotifications.CountAsync(n => n.RecipientUserId == recipientUserId, cancellationToken);

    /// <inheritdoc />
    public Task<int> CountUnreadForRecipientAsync(Guid recipientUserId, CancellationToken cancellationToken) =>
        context.InAppNotifications.CountAsync(n => n.RecipientUserId == recipientUserId && n.ReadAtUtc == null, cancellationToken);
}
