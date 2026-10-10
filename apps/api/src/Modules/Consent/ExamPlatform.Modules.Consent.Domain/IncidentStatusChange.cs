using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Consent.Domain;

/// <summary>
/// One entry in an incident's status history: the move from one status to another, the note that justified it, and who made it
/// and when. Entries are never edited or removed, so the history shows every change in the order it happened.
/// </summary>
public sealed class IncidentStatusChange : Entity
{
    /// <summary>The status before the change.</summary>
    public IncidentStatus FromStatus { get; private set; }

    /// <summary>The status after the change.</summary>
    public IncidentStatus ToStatus { get; private set; }

    /// <summary>Why the change was made.</summary>
    public string Note { get; private set; }

    /// <summary>The staff member who made the change.</summary>
    public Guid ChangedById { get; private set; }

    /// <summary>When the change was made, in UTC.</summary>
    public DateTime ChangedAtUtc { get; private set; }

    // Created only by Incident.ChangeStatus, which checks the move first. Internal so the aggregate can create it, and so EF
    // Core can rebuild a stored entry through it.
    internal IncidentStatusChange(
        Guid id,
        IncidentStatus fromStatus,
        IncidentStatus toStatus,
        string note,
        Guid changedById,
        DateTime changedAtUtc)
        : base(id)
    {
        FromStatus = fromStatus;
        ToStatus = toStatus;
        Note = note;
        ChangedById = changedById;
        ChangedAtUtc = changedAtUtc;
    }
}
