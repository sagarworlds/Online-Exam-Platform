using ExamPlatform.Modules.Consent.Domain.Exceptions;
using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Consent.Domain;

/// <summary>
/// An incident or breach the platform must log and escalate (FR-52, NFR-13). Its escalation due time runs from when the incident
/// was <em>detected</em>, not from when someone logged it, so an incident logged late is overdue on arrival. It is overdue while
/// still <see cref="IncidentStatus.Logged"/> past that time. Every status change is kept, with who made it and when.
/// </summary>
public sealed class Incident : AggregateRoot
{
    /// <summary>The longest description accepted.</summary>
    public const int MaxDescriptionLength = 2000;

    /// <summary>The longest affected-data description accepted.</summary>
    public const int MaxAffectedDataLength = 1000;

    /// <summary>The longest note accepted with a status change.</summary>
    public const int MaxNoteLength = 1000;

    private readonly List<IncidentStatusChange> _statusChanges = [];

    /// <summary>What happened.</summary>
    public string Description { get; private set; }

    /// <summary>When the incident was detected, in UTC. The escalation clock starts here.</summary>
    public DateTime DetectedAtUtc { get; private set; }

    /// <summary>The kind of incident.</summary>
    public IncidentCategory Category { get; private set; }

    /// <summary>The personal data that may be affected, or a statement that it is not yet known.</summary>
    public string AffectedData { get; private set; }

    /// <summary>Where the incident stands now.</summary>
    public IncidentStatus Status { get; private set; }

    /// <summary>The staff member who logged the incident.</summary>
    public Guid LoggedById { get; private set; }

    /// <summary>When the incident was logged, in UTC. This can be later than <see cref="DetectedAtUtc"/>.</summary>
    public DateTime LoggedAtUtc { get; private set; }

    /// <summary>When the incident falls due for escalation: <see cref="DetectedAtUtc"/> plus <see cref="IncidentEscalation.Window"/>.</summary>
    public DateTime EscalationDueAtUtc { get; private set; }

    /// <summary>Every status change since the incident was logged, oldest first.</summary>
    public IReadOnlyList<IncidentStatusChange> StatusChanges => _statusChanges.AsReadOnly();

    // Used by EF Core to rebuild a stored incident. Callers use Log, which checks the rules first.
    private Incident(
        Guid id,
        string description,
        DateTime detectedAtUtc,
        IncidentCategory category,
        string affectedData,
        IncidentStatus status,
        Guid loggedById,
        DateTime loggedAtUtc,
        DateTime escalationDueAtUtc)
        : base(id)
    {
        Description = description;
        DetectedAtUtc = detectedAtUtc;
        Category = category;
        AffectedData = affectedData;
        Status = status;
        LoggedById = loggedById;
        LoggedAtUtc = loggedAtUtc;
        EscalationDueAtUtc = escalationDueAtUtc;
    }

    /// <summary>Logs a new incident. It starts as <see cref="IncidentStatus.Logged"/>, due for escalation six hours after detection.</summary>
    /// <param name="description">What happened. Required, at most <see cref="MaxDescriptionLength"/> characters.</param>
    /// <param name="detectedAtUtc">When the incident was detected. Must be a UTC instant, and not later than <paramref name="nowUtc"/>.</param>
    /// <param name="category">The kind of incident.</param>
    /// <param name="affectedData">The personal data that may be affected, or that it is not yet known. Required.</param>
    /// <param name="loggedById">The staff member logging the incident.</param>
    /// <param name="nowUtc">The current instant. It stamps when the incident was logged.</param>
    /// <returns>The new incident.</returns>
    /// <exception cref="InvalidIncidentError">
    /// A text field is blank or too long, the category is unknown, or the detection time is not a UTC instant or lies in the future.
    /// </exception>
    public static Incident Log(
        string description,
        DateTime detectedAtUtc,
        IncidentCategory category,
        string affectedData,
        Guid loggedById,
        DateTime nowUtc)
    {
        var text = RequireText(description, MaxDescriptionLength, "description");
        var affected = RequireText(affectedData, MaxAffectedDataLength, "affected-data description");
        if (!Enum.IsDefined(category))
        {
            throw new InvalidIncidentError("The incident category is not one the log knows.");
        }

        RequireDetectionTime(detectedAtUtc, nowUtc);

        return new Incident(
            Guid.NewGuid(),
            text,
            detectedAtUtc,
            category,
            affected,
            IncidentStatus.Logged,
            loggedById,
            nowUtc,
            IncidentEscalation.DueAt(detectedAtUtc));
    }

    /// <summary>
    /// Moves the incident to another status and records who made the change, when, and why. The note is required because the
    /// history is the record of what was decided, and it is the only place the reason is kept.
    /// </summary>
    /// <param name="to">The status to move to.</param>
    /// <param name="note">Why the change is made. Required, at most <see cref="MaxNoteLength"/> characters.</param>
    /// <param name="changedById">The staff member making the change.</param>
    /// <param name="nowUtc">The current instant. It is recorded as the change time.</param>
    /// <exception cref="InvalidIncidentError">The note is blank or too long, or the status is unknown.</exception>
    /// <exception cref="IncidentStatusChangeNotAllowedError">The lifecycle does not allow the move. The incident is left unchanged.</exception>
    public void ChangeStatus(IncidentStatus to, string note, Guid changedById, DateTime nowUtc)
    {
        if (!Enum.IsDefined(to))
        {
            throw new InvalidIncidentError("The incident status is not one the log knows.");
        }

        var reason = RequireText(note, MaxNoteLength, "note");
        if (!IsAllowedTransition(Status, to))
        {
            throw new IncidentStatusChangeNotAllowedError(Status, to);
        }

        _statusChanges.Add(new IncidentStatusChange(Guid.NewGuid(), Status, to, reason, changedById, nowUtc));
        Status = to;
    }

    /// <summary>Whether the incident is overdue at the given instant: still logged, and past its escalation due time.</summary>
    /// <param name="nowUtc">The instant to test.</param>
    /// <returns>True when the incident is overdue.</returns>
    public bool IsOverdueAt(DateTime nowUtc) => IncidentEscalation.IsOverdue(Status, EscalationDueAtUtc, nowUtc);

    // The lifecycle. Logged is the only state with two exits, to Reported or straight to Closed (a false alarm needs no
    // report). A reported incident never returns to Logged, because the authority has already been told. Closed has no exit,
    // so a finished record cannot be rewritten after the fact.
    private static bool IsAllowedTransition(IncidentStatus from, IncidentStatus to) => (from, to) switch
    {
        (IncidentStatus.Logged, IncidentStatus.Reported) => true,
        (IncidentStatus.Logged, IncidentStatus.Closed) => true,
        (IncidentStatus.Reported, IncidentStatus.Closed) => true,
        _ => false,
    };

    // Trims the text and refuses it when it is blank or too long. The field name goes into the message, never the text itself.
    private static string RequireText(string? value, int maxLength, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidIncidentError($"The {field} cannot be empty.");
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new InvalidIncidentError($"The {field} can be at most {maxLength} characters.");
        }

        return trimmed;
    }

    // The clock starts at the detection time, so that time must be a real instant. An Unspecified or Local time could be read
    // against the wrong zone, which would move the due time. A future time would let an incident start its clock late.
    private static void RequireDetectionTime(DateTime detectedAtUtc, DateTime nowUtc)
    {
        if (detectedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new InvalidIncidentError("The detection time must be a UTC instant.");
        }

        if (detectedAtUtc > nowUtc)
        {
            throw new InvalidIncidentError("The detection time cannot be in the future.");
        }
    }
}
