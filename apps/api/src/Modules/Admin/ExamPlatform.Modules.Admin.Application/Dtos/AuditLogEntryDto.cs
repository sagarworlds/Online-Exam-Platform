namespace ExamPlatform.Modules.Admin.Application.Dtos;

/// <summary>Read-only projection of one audit log entry.</summary>
/// <param name="Id">The entry's id.</param>
/// <param name="OccurredAtUtc">When the audited action happened.</param>
/// <param name="ActorUserId">Who performed the action, if any.</param>
/// <param name="ActorRole">A snapshot of the actor's role name.</param>
/// <param name="Action">The action code.</param>
/// <param name="EntityType">The kind of entity affected.</param>
/// <param name="EntityId">The affected entity's id.</param>
/// <param name="Metadata">Free-form structured context.</param>
public sealed record AuditLogEntryDto(
    Guid Id,
    DateTime OccurredAtUtc,
    Guid? ActorUserId,
    string? ActorRole,
    string Action,
    string EntityType,
    string EntityId,
    IReadOnlyDictionary<string, string> Metadata);
