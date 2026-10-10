namespace ExamPlatform.Modules.Consent.Domain;

/// <summary>
/// The escalation timer for incidents (FR-52, NFR-13). It is the single place that decides the due time and the overdue flag,
/// so the window cannot drift between the logging path and the listing path.
/// </summary>
public static class IncidentEscalation
{
    /// <summary>
    /// How long an incident may stay unreported after it was detected. NFR-13 and section 7.3 of the requirements set six hours,
    /// taken from the CERT-In directions of 2022. The window runs from detection, not from logging, because the duty starts when
    /// the platform noticed the incident.
    /// </summary>
    public static readonly TimeSpan Window = TimeSpan.FromHours(6);

    /// <summary>Works out when an incident detected at the given instant falls due for escalation.</summary>
    /// <param name="detectedAtUtc">When the incident was detected, as a UTC instant.</param>
    /// <returns>The detection time plus <see cref="Window"/>.</returns>
    public static DateTime DueAt(DateTime detectedAtUtc) => detectedAtUtc + Window;

    /// <summary>
    /// Whether an incident is overdue. Only a <see cref="IncidentStatus.Logged"/> incident can be overdue. Once an outside
    /// authority has been notified, the six-hour duty is met, and the remaining manual steps are not timed by NFR-13.
    /// The due instant itself is not overdue. The incident becomes overdue only after it.
    /// </summary>
    /// <param name="status">The incident's current status.</param>
    /// <param name="dueAtUtc">The incident's escalation due time.</param>
    /// <param name="nowUtc">The instant to test.</param>
    /// <returns>True when the incident is still logged and its due time has passed.</returns>
    public static bool IsOverdue(IncidentStatus status, DateTime dueAtUtc, DateTime nowUtc) =>
        status == IncidentStatus.Logged && nowUtc > dueAtUtc;
}
