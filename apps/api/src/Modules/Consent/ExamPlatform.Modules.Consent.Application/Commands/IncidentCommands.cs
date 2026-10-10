using ExamPlatform.Modules.Consent.Domain;

namespace ExamPlatform.Modules.Consent.Application.Commands;

/// <summary>Logs a new incident and starts its escalation timer (FR-52).</summary>
/// <param name="Description">What happened.</param>
/// <param name="DetectedAtUtc">When it was detected. A UTC instant, not later than now.</param>
/// <param name="Category">The kind of incident.</param>
/// <param name="AffectedData">The personal data that may be affected, or that it is not yet known.</param>
/// <param name="LoggedById">The staff member logging it.</param>
public sealed record LogIncidentCommand(
    string Description,
    DateTime DetectedAtUtc,
    IncidentCategory Category,
    string AffectedData,
    Guid LoggedById);

/// <summary>Moves an incident to another status, with a note saying why.</summary>
/// <param name="IncidentId">The incident to change.</param>
/// <param name="Status">The status to move to.</param>
/// <param name="Note">Why the change is made.</param>
/// <param name="ChangedById">The staff member making the change.</param>
public sealed record ChangeIncidentStatusCommand(
    Guid IncidentId,
    IncidentStatus Status,
    string Note,
    Guid ChangedById);
