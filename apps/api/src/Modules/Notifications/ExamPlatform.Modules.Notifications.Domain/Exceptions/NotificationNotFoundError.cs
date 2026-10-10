using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Notifications.Domain.Exceptions;

/// <summary>No notice of the signed-in account has that id. A notice that belongs to someone else answers the same way, so ids cannot be probed.</summary>
public sealed class NotificationNotFoundError() : DomainException("No notification matches the given id.")
{
    /// <inheritdoc />
    public override string ErrorCode => "notification_not_found";

    /// <inheritdoc />
    public override int HttpStatusCode => 404;
}
