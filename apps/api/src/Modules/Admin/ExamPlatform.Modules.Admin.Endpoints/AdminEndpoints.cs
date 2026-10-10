using ExamPlatform.Modules.Admin.Application.Queries;
using ExamPlatform.SharedKernel.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ExamPlatform.Modules.Admin.Endpoints;

/// <summary>Maps the Admin module's HTTP endpoints (FR-40).</summary>
public static class AdminEndpoints
{
    /// <summary>Maps <c>GET /v1/admin/audit-logs</c>.</summary>
    /// <param name="endpoints">The endpoint route builder to map onto.</param>
    public static void MapAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/v1/admin/audit-logs", async (
                string? entityType, Guid? actorUserId, int? page, int? pageSize,
                SearchAuditLogsHandler handler, CancellationToken ct) =>
            {
                var query = new SearchAuditLogsQuery(entityType, actorUserId, PageRequest.Create(page, pageSize));
                var results = await handler.HandleAsync(query, ct);
                return Results.Ok(results);
            })
            .WithTags("Admin")
            .RequireAuthorization("permission:admin.audit.read");

        endpoints.MapBrandingEndpoints();
    }
}
