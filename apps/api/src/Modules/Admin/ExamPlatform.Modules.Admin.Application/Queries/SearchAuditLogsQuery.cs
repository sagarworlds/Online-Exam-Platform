namespace ExamPlatform.Modules.Admin.Application.Queries;

/// <summary>Searches the audit trail (FR-40), paged and optionally filtered.</summary>
/// <param name="EntityType">Only entries for this entity type, if given.</param>
/// <param name="ActorUserId">Only entries by this actor, if given.</param>
/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">Number of entries per page.</param>
public sealed record SearchAuditLogsQuery(string? EntityType, Guid? ActorUserId, int Page, int PageSize);
