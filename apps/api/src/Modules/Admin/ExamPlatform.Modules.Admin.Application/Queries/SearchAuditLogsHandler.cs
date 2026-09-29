using ExamPlatform.Modules.Admin.Application.Dtos;
using ExamPlatform.Modules.Admin.Application.Ports;

namespace ExamPlatform.Modules.Admin.Application.Queries;

/// <summary>Handles <see cref="SearchAuditLogsQuery"/>.</summary>
public sealed class SearchAuditLogsHandler(IAuditLogRepository repository)
{
    /// <summary>Runs the search and projects results for the admin UI.</summary>
    /// <param name="query">The filter and paging parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<AuditLogEntryDto>> HandleAsync(SearchAuditLogsQuery query, CancellationToken cancellationToken)
    {
        var entries = await repository.SearchAsync(
            query.EntityType, query.ActorUserId, query.Page, query.PageSize, cancellationToken);

        return entries
            .Select(e => new AuditLogEntryDto(e.Id, e.OccurredAtUtc, e.ActorUserId, e.ActorRole, e.Action, e.EntityType, e.EntityId, e.Metadata))
            .ToList();
    }
}
