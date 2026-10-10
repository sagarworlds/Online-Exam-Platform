using ExamPlatform.Modules.Notifications.Application.Ports;
using ExamPlatform.Modules.Notifications.Domain;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.Notifications.UnitTests;

/// <summary>
/// A store with the database's behaviour: <see cref="Notices"/> is what has been saved, and what is added waits in a pending set until a save
/// commits it, so a failed save leaves nothing behind, as it does for the real store.
/// </summary>
internal sealed class InMemoryNotices : IInAppNotificationRepository
{
    private readonly List<InAppNotification> _pending = [];

    /// <summary>The notices that have been saved. A test may add to it directly to play a notice another request already saved.</summary>
    public List<InAppNotification> Notices { get; } = [];

    public Task<bool> ExistsAsync(Guid recipientUserId, NoticeKind kind, Guid subjectId, CancellationToken cancellationToken) =>
        Task.FromResult(Notices.Any(n => n.RecipientUserId == recipientUserId && n.Kind == kind && n.SubjectId == subjectId));

    public Task<IReadOnlyList<(Guid RecipientUserId, Guid SubjectId)>> ListRecordedAsync(
        NoticeKind kind, IReadOnlyCollection<Guid> subjectIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<(Guid RecipientUserId, Guid SubjectId)>>(
            Notices.Where(n => n.Kind == kind && subjectIds.Contains(n.SubjectId)).Select(n => (n.RecipientUserId, n.SubjectId)).ToList());

    public void Add(InAppNotification notification) => _pending.Add(notification);

    /// <summary>Saves what is pending.</summary>
    public void Commit()
    {
        Notices.AddRange(_pending);
        _pending.Clear();
    }

    /// <summary>Drops what is pending, as the real unit of work does after a failed save.</summary>
    public void Discard() => _pending.Clear();

    public Task<InAppNotification?> GetForRecipientAsync(Guid id, Guid recipientUserId, CancellationToken cancellationToken) =>
        Task.FromResult(Notices.FirstOrDefault(n => n.Id == id && n.RecipientUserId == recipientUserId));

    public Task<IReadOnlyList<InAppNotification>> ListUnreadForRecipientAsync(Guid recipientUserId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<InAppNotification>>(Notices.Where(n => n.RecipientUserId == recipientUserId && !n.IsRead).ToList());

    public Task<IReadOnlyList<InAppNotification>> ListPageForRecipientAsync(Guid recipientUserId, PageRequest page, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<InAppNotification>>(Notices.Where(n => n.RecipientUserId == recipientUserId)
            .OrderBy(n => n.IsRead ? 1 : 0).ThenByDescending(n => n.CreatedAtUtc).Skip(page.Skip).Take(page.PageSize).ToList());

    public Task<int> CountForRecipientAsync(Guid recipientUserId, CancellationToken cancellationToken) =>
        Task.FromResult(Notices.Count(n => n.RecipientUserId == recipientUserId));

    public Task<int> CountUnreadForRecipientAsync(Guid recipientUserId, CancellationToken cancellationToken) =>
        Task.FromResult(Notices.Count(n => n.RecipientUserId == recipientUserId && !n.IsRead));
}

/// <summary>Saves what is pending, or fails the way a unique index or a dropped connection would, and drops what was pending.</summary>
internal sealed class FakeNotificationUnitOfWork(InMemoryNotices store) : IInAppNotificationUnitOfWork
{
    /// <summary>How many saves succeeded.</summary>
    public int Saves { get; private set; }

    /// <summary>Runs just before the next save, to play another request that writes at the same moment.</summary>
    public Action? BeforeNextSave { get; set; }

    /// <summary>When set, the next save fails with what this returns.</summary>
    public Func<Exception>? FailNextSaveWith { get; set; }

    /// <summary>When set, every save fails with what this returns.</summary>
    public Func<Exception>? FailEverySaveWith { get; set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        BeforeNextSave?.Invoke();
        BeforeNextSave = null;

        var failure = FailNextSaveWith?.Invoke() ?? FailEverySaveWith?.Invoke();
        FailNextSaveWith = null;
        if (failure is not null)
        {
            store.Discard();
            throw failure;
        }

        store.Commit();
        Saves++;
        return Task.FromResult(1);
    }
}

/// <summary>A clock that always says the same instant.</summary>
internal sealed class FixedClock(DateTime utcNow) : Clock
{
    public DateTime UtcNow => utcNow;
}
