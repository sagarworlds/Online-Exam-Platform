using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Admin.Domain;

/// <summary>
/// One immutable entry in the platform's audit trail (FR-40). Append-only: there
/// is deliberately no method to modify or remove an entry once created.
/// </summary>
public sealed class AuditLog : Entity
{
    /// <summary>When the audited action happened.</summary>
    public DateTime OccurredAtUtc { get; private set; }

    /// <summary>Who performed the action, if any (some actions are system-initiated).</summary>
    public Guid? ActorUserId { get; private set; }

    /// <summary>A snapshot of the actor's role name at the time of the action — roles can change later.</summary>
    public string? ActorRole { get; private set; }

    /// <summary>A short, stable action code (e.g. "Consent.Withdrawn", "Identity.RoleAssigned").</summary>
    public string Action { get; private set; }

    /// <summary>The kind of entity affected (e.g. "ConsentRecord", "User").</summary>
    public string EntityType { get; private set; }

    /// <summary>The affected entity's id.</summary>
    public string EntityId { get; private set; }

    /// <summary>Free-form structured context about the action, for admin review.</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; private set; }

    /// <summary>Correlates this entry with the request/trace that caused it.</summary>
    public string? CorrelationId { get; private set; }

    private AuditLog(
        Guid id,
        DateTime occurredAtUtc,
        Guid? actorUserId,
        string? actorRole,
        string action,
        string entityType,
        string entityId,
        IReadOnlyDictionary<string, string> metadata,
        string? correlationId) : base(id)
    {
        OccurredAtUtc = occurredAtUtc;
        ActorUserId = actorUserId;
        ActorRole = actorRole;
        Action = action;
        EntityType = entityType;
        EntityId = entityId;
        Metadata = metadata;
        CorrelationId = correlationId;
    }

    /// <summary>Creates a new audit log entry.</summary>
    /// <param name="nowUtc">The current instant, stamped by the logger — never supplied by the caller, so every entry's timestamp is trustworthy.</param>
    /// <param name="actorUserId">Who performed the action, if any.</param>
    /// <param name="actorRole">A snapshot of the actor's role name.</param>
    /// <param name="action">A short, stable action code.</param>
    /// <param name="entityType">The kind of entity affected.</param>
    /// <param name="entityId">The affected entity's id.</param>
    /// <param name="metadata">Free-form structured context.</param>
    /// <param name="correlationId">Correlates this entry with the causing request/trace.</param>
    public static AuditLog Create(
        DateTime nowUtc,
        Guid? actorUserId,
        string? actorRole,
        string action,
        string entityType,
        string entityId,
        IReadOnlyDictionary<string, string> metadata,
        string? correlationId) =>
        new(Guid.NewGuid(), nowUtc, actorUserId, actorRole, action, entityType, entityId, metadata, correlationId);
}
