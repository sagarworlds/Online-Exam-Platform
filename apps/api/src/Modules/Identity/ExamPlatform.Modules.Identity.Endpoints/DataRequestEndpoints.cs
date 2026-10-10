using ExamPlatform.Modules.Identity.Application.DataRequests;
using ExamPlatform.Modules.Identity.Domain.DataRequests;
using ExamPlatform.SharedKernel.Application.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ExamPlatform.Modules.Identity.Endpoints;

/// <summary>
/// Maps the data-principal request routes (FR-48): a candidate's own requests under <c>/v1/me/data-requests</c>, and the staff queue under
/// <c>/v1/data-requests</c>.
/// </summary>
internal static class DataRequestEndpoints
{
    // Staff answer data requests with the privacy-operations permission. A dedicated permission can follow once the owner names one.
    private const string StaffPermission = "permission:consent.manage";

    /// <summary>Maps the candidate and staff data-request routes.</summary>
    /// <param name="endpoints">The endpoint route builder to map onto.</param>
    public static void MapDataRequestEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var mine = endpoints.MapGroup("/v1/me/data-requests").WithTags("Identity").RequireAuthorization();

        mine.MapPost("/", async (RaiseDataRequestHttpRequest request, HttpContext http, RaiseDataRequestHandler handler, CancellationToken ct) =>
        {
            var raised = await handler.HandleAsync(http.User.GetUserId(), request.Kind, request.Details, ct);
            return Results.Ok(raised);
        });

        mine.MapGet("/", async (HttpContext http, DataRequestQueries queries, CancellationToken ct) =>
        {
            var requests = await queries.ListMineAsync(http.User.GetUserId(), ct);
            return Results.Ok(requests);
        });

        var staff = endpoints.MapGroup("/v1/data-requests").WithTags("Identity").RequireAuthorization(StaffPermission);

        staff.MapGet("/open", async (DataRequestQueries queries, CancellationToken ct) =>
        {
            var requests = await queries.ListOpenAsync(ct);
            return Results.Ok(requests);
        });

        staff.MapPost("/{id:guid}/resolve", async (
            Guid id, ResolveDataRequestHttpRequest request, HttpContext http, ResolveDataRequestHandler handler, CancellationToken ct) =>
        {
            var resolved = await handler.HandleAsync(id, http.User.GetUserId(), request.Outcome, request.Note, ct);
            return Results.Ok(resolved);
        });
    }
}

/// <summary>Body of <c>POST /v1/me/data-requests</c>. The account comes from the token, never from the body.</summary>
/// <param name="Kind">Access, correction or erasure.</param>
/// <param name="Details">What the candidate wants done, or null.</param>
public sealed record RaiseDataRequestHttpRequest(DataRequestKind Kind, string? Details);

/// <summary>Body of <c>POST /v1/data-requests/{id}/resolve</c>.</summary>
/// <param name="Outcome">Completed or Rejected.</param>
/// <param name="Note">What was done, or why it was refused. Required for a refusal.</param>
public sealed record ResolveDataRequestHttpRequest(DataRequestStatus Outcome, string? Note);
