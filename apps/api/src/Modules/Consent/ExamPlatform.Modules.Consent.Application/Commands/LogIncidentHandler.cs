using ExamPlatform.Modules.Admin.Contracts;
using ExamPlatform.Modules.Consent.Application.Dtos;
using ExamPlatform.Modules.Consent.Application.Ports;
using ExamPlatform.Modules.Consent.Domain;
using ExamPlatform.Modules.Consent.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.Consent.Application.Commands;

/// <summary>Handles <see cref="LogIncidentCommand"/>: records a new incident and starts its escalation timer (FR-52).</summary>
/// <param name="repository">Stores the incident.</param>
/// <param name="unitOfWork">Commits the change.</param>
/// <param name="auditLogger">Records the logging in the audit trail (FR-40).</param>
/// <param name="clock">Supplies the logging time, and the instant the detection time is checked against.</param>
public sealed class LogIncidentHandler(
    IIncidentRepository repository,
    IConsentUnitOfWork unitOfWork,
    IAuditLogger auditLogger,
    Clock clock)
{
    /// <summary>
    /// Logs the incident, saves it, and writes an audit entry. The entry carries the incident's id and category only. The text
    /// of the incident may name people, so it stays out of the audit trail and the logs.
    /// </summary>
    /// <param name="command">The incident to log.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The logged incident, as it stands now.</returns>
    /// <exception cref="InvalidIncidentError">A field is blank or too long, the category is unknown, or the detection time is not a UTC instant or lies in the future.</exception>
    public async Task<IncidentDto> HandleAsync(LogIncidentCommand command, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var incident = Incident.Log(
            command.Description,
            command.DetectedAtUtc,
            command.Category,
            command.AffectedData,
            command.LoggedById,
            now);

        await repository.AddAsync(incident, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await auditLogger.RecordAsync(
            new AuditEntry(
                ActorUserId: command.LoggedById,
                ActorRole: null,
                Action: "Incident.Logged",
                EntityType: "Incident",
                EntityId: incident.Id.ToString(),
                Metadata: new Dictionary<string, string> { ["category"] = incident.Category.ToString() },
                CorrelationId: null),
            cancellationToken);

        return IncidentDto.From(incident, now);
    }
}
