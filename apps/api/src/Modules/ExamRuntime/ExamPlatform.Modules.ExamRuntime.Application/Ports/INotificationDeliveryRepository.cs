using ExamPlatform.Modules.ExamRuntime.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Application.Ports;

/// <summary>Persistence port for <see cref="NotificationDelivery"/>, the record of which scheduled e-mails were already sent (FR-39).</summary>
public interface INotificationDeliveryRepository
{
    /// <summary>Starts tracking a new record; it is stored when the unit of work saves.</summary>
    /// <param name="delivery">The record to add.</param>
    void Add(NotificationDelivery delivery);

    /// <summary>Loads the records of one kind about any of the given subjects, tracked so recording a try is saved.</summary>
    /// <param name="kind">Which e-mail.</param>
    /// <param name="subjectIds">The subjects (exams, attempts or revisions, according to the kind).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<NotificationDelivery>> ListAsync(NotificationKind kind, IReadOnlyCollection<Guid> subjectIds, CancellationToken cancellationToken);
}
