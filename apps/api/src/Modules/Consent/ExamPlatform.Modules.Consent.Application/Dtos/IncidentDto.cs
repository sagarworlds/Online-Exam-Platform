using ExamPlatform.Modules.Consent.Domain;

namespace ExamPlatform.Modules.Consent.Application.Dtos;

/// <summary>
/// An incident as staff see it. <see cref="IsOverdue"/> is worked out at the moment the incident is read, so the flag is current
/// on every read and nothing has to run in the background to set it.
/// </summary>
/// <param name="Id">The incident's identifier.</param>
/// <param name="Description">What happened.</param>
/// <param name="DetectedAtUtc">When the incident was detected, in UTC.</param>
/// <param name="Category">The kind of incident.</param>
/// <param name="AffectedData">The personal data that may be affected, or a statement that it is not yet known.</param>
/// <param name="Status">Where the incident stands now.</param>
/// <param name="LoggedById">The staff member who logged it.</param>
/// <param name="LoggedAtUtc">When it was logged, in UTC.</param>
/// <param name="EscalationDueAtUtc">When it falls due for escalation, in UTC.</param>
/// <param name="IsOverdue">Whether it is still logged past its escalation due time at the moment it was read.</param>
/// <param name="StatusChanges">Every status change, oldest first.</param>
public sealed record IncidentDto(
    Guid Id,
    string Description,
    DateTime DetectedAtUtc,
    IncidentCategory Category,
    string AffectedData,
    IncidentStatus Status,
    Guid LoggedById,
    DateTime LoggedAtUtc,
    DateTime EscalationDueAtUtc,
    bool IsOverdue,
    IReadOnlyList<IncidentStatusChangeDto> StatusChanges)
{
    /// <summary>Builds the view of an incident as it stands at the given instant.</summary>
    /// <param name="incident">The incident to show, with its status history loaded.</param>
    /// <param name="nowUtc">The instant the overdue flag is worked out at.</param>
    /// <returns>The incident's view.</returns>
    public static IncidentDto From(Incident incident, DateTime nowUtc) => new(
        incident.Id,
        incident.Description,
        incident.DetectedAtUtc,
        incident.Category,
        incident.AffectedData,
        incident.Status,
        incident.LoggedById,
        incident.LoggedAtUtc,
        incident.EscalationDueAtUtc,
        incident.IsOverdueAt(nowUtc),
        incident.StatusChanges
            .Select(c => new IncidentStatusChangeDto(c.FromStatus, c.ToStatus, c.Note, c.ChangedById, c.ChangedAtUtc))
            .ToList());
}

/// <summary>One status change in an incident's history.</summary>
/// <param name="FromStatus">The status before the change.</param>
/// <param name="ToStatus">The status after the change.</param>
/// <param name="Note">Why the change was made.</param>
/// <param name="ChangedById">The staff member who made the change.</param>
/// <param name="ChangedAtUtc">When the change was made, in UTC.</param>
public sealed record IncidentStatusChangeDto(
    IncidentStatus FromStatus,
    IncidentStatus ToStatus,
    string Note,
    Guid ChangedById,
    DateTime ChangedAtUtc);
