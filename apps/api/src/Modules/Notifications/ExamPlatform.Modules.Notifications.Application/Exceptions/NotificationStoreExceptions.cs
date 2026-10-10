namespace ExamPlatform.Modules.Notifications.Application.Exceptions;

/// <summary>
/// The store refused a notice because the same recipient, kind and subject is already recorded. It is how the store reports that the
/// notice is already in the feed, so the caller treats it as success.
/// </summary>
/// <param name="innerException">The persistence-layer exception that detected the duplicate.</param>
public sealed class NotificationAlreadyRecordedException(Exception innerException)
    : Exception("The notification is already recorded.", innerException);

/// <summary>
/// The store could not save notifications, for a reason other than a duplicate (the database is unreachable, for example). Raised by the
/// unit of work and caught by the in-app notifier, which logs it.
/// </summary>
/// <param name="message">What failed, for the log.</param>
/// <param name="innerException">The persistence-layer exception.</param>
public sealed class NotificationStoreException(string message, Exception innerException) : Exception(message, innerException);
