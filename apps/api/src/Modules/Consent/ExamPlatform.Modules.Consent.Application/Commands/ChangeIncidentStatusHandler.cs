using ExamPlatform.Modules.Admin.Contracts;
using ExamPlatform.Modules.Consent.Application.Dtos;
using ExamPlatform.Modules.Consent.Application.Exceptions;
using ExamPlatform.Modules.Consent.Application.Ports;
using ExamPlatform.Modules.Consent.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.Consent.Application.Commands;

/// <summary>Handles <see cref="ChangeIncidentStatusCommand"/>: moves an incident to another status and records who did it (FR-52).</summary>
/// <param name="repository">Finds the incident.</param>
/// <param name="unitOfWork">Commits the change. A concurrent change to the same incident is refused with a conflict.</param>
/// <param name="auditLogger">Records the change in the audit trail (FR-40).</param>
/// <param name="clock">Supplies the change time.</param>
public sealed class ChangeIncidentStatusHandler(
    IIncidentRepository repository,
    IConsentUnitOfWork unitOfWork,
    IAuditLogger auditLogger,
    Clock clock)
{
    /// <summary>
    /// Applies the status change, saves it, and writes an audit entry with the two statuses. The note is kept in the incident's
    /// history only, not in the audit trail, for the same reason as the incident text.
    /// </summary>
    /// <param name="command">The incident, the status to move to, the note and the staff member.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The incident after the change.</returns>
    /// <exception cref="IncidentNotFoundError">No incident has that id.</exception>
    /// <exception cref="InvalidIncidentError">The note is blank or too long, or the status is unknown.</exception>
    /// <exception cref="IncidentStatusChangeNotAllowedError">The lifecycle does not allow the move. Nothing is saved.</exception>
    public async Task<IncidentDto> HandleAsync(ChangeIncidentStatusCommand command, CancellationToken cancellationToken)
    {
        var incident = await repository.GetByIdAsync(command.IncidentId, cancellationToken)
            ?? throw new IncidentNotFoundError();

        var now = clock.UtcNow;
        var previousStatus = incident.Status;
        incident.ChangeStatus(command.Status, command.Note, command.ChangedById, now);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await auditLogger.RecordAsync(
            new AuditEntry(
                ActorUserId: command.ChangedById,
                ActorRole: null,
                Action: "Incident.StatusChanged",
                EntityType: "Incident",
                EntityId: incident.Id.ToString(),
                Metadata: new Dictionary<string, string>
                {
                    ["from"] = previousStatus.ToString(),
                    ["to"] = incident.Status.ToString(),
                },
                CorrelationId: null),
            cancellationToken);

        return IncidentDto.From(incident, now);
    }
}
