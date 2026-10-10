namespace ExamPlatform.Modules.Notifications.Application.Ports;

/// <summary>Saves the notifications module's pending changes in one transaction.</summary>
public interface IInAppNotificationUnitOfWork
{
    /// <summary>Saves the tracked changes.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of rows written.</returns>
    /// <exception cref="Exceptions.NotificationAlreadyRecordedException">A notice with the same recipient, kind and subject was saved first (by a concurrent request).</exception>
    /// <exception cref="Exceptions.NotificationStoreException">The changes could not be saved for any other reason.</exception>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
