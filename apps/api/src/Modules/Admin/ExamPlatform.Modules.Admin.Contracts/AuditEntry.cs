namespace ExamPlatform.Modules.Admin.Contracts;

/// <summary>
/// One fact to record in the audit trail. Carries the actor's identity as plain
/// scalars (never a navigation property to another module's <c>User</c> type),
/// so Admin has zero schema knowledge of Identity or any other module (FR-40).
/// </summary>
/// <param name="ActorUserId">The user who performed the action, if any (some actions are system-initiated).</param>
/// <param name="ActorRole">A snapshot of the actor's role name at the time of the action — roles can change later.</param>
/// <param name="Action">A short, stable action code (e.g. "Consent.Withdrawn", "Identity.RoleAssigned").</param>
/// <param name="EntityType">The kind of entity affected (e.g. "ConsentRecord", "User").</param>
/// <param name="EntityId">The affected entity's id.</param>
/// <param name="Metadata">Free-form structured context about the action, for admin review.</param>
/// <param name="CorrelationId">Correlates this entry with the request/trace that caused it.</param>
public sealed record AuditEntry(
    Guid? ActorUserId,
    string? ActorRole,
    string Action,
    string EntityType,
    string EntityId,
    IReadOnlyDictionary<string, string> Metadata,
    string? CorrelationId);
