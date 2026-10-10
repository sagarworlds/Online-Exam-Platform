using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Consent.Domain.Exceptions;

/// <summary>
/// The incident cannot move from its current status to the requested one (409). The lifecycle only moves forward:
/// a reported incident never returns to logged, a closed incident never changes, and a status cannot be set to itself.
/// </summary>
/// <param name="from">The status the incident is in.</param>
/// <param name="to">The status that was requested.</param>
public sealed class IncidentStatusChangeNotAllowedError(IncidentStatus from, IncidentStatus to)
    : DomainException($"An incident with status {from} cannot be changed to {to}.")
{
    /// <inheritdoc />
    public override string ErrorCode => "incident_status_change_not_allowed";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}
