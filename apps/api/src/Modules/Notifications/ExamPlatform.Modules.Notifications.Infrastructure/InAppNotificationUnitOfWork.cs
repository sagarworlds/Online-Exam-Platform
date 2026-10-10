using ExamPlatform.Modules.Notifications.Application.Exceptions;
using ExamPlatform.Modules.Notifications.Application.Ports;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ExamPlatform.Modules.Notifications.Infrastructure;

/// <summary>
/// EF Core-backed <see cref="IInAppNotificationUnitOfWork"/>. It translates the store's failures into the module's two signals, and clears
/// the change tracker when a save fails: a notice that was refused must not be sent again by the next save on the same scope, which a
/// notification run does for every notice it records.
/// </summary>
public sealed class InAppNotificationUnitOfWork(NotificationsDbContext context) : IInAppNotificationUnitOfWork
{
    // Postgres' code for a unique index being violated (SQLSTATE 23505).
    private const string UniqueViolation = "23505";

    /// <inheritdoc />
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            context.ChangeTracker.Clear();
            throw new NotificationAlreadyRecordedException(exception);
        }
        catch (Exception exception) when (exception is DbUpdateException or NpgsqlException)
        {
            context.ChangeTracker.Clear();
            throw new NotificationStoreException("The notifications could not be saved.", exception);
        }
    }
}
