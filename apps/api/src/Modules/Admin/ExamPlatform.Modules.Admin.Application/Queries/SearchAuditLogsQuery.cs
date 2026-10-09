using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.Admin.Application.Queries;

/// <summary>Searches the audit trail (FR-40), paged and optionally filtered.</summary>
/// <param name="EntityType">Only entries for this entity type, if given.</param>
/// <param name="ActorUserId">Only entries by this actor, if given.</param>
/// <param name="Paging">Which page to read; already validated, see <see cref="PageRequest.Create"/>.</param>
public sealed record SearchAuditLogsQuery(string? EntityType, Guid? ActorUserId, PageRequest Paging);
