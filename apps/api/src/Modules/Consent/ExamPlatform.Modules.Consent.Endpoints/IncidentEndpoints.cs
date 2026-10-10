using ExamPlatform.Modules.Consent.Application.Commands;
using ExamPlatform.Modules.Consent.Application.Dtos;
using ExamPlatform.Modules.Consent.Application.Queries;
using ExamPlatform.Modules.Consent.Domain;
using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Application.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ExamPlatform.Modules.Consent.Endpoints;

/// <summary>Maps the incident and breach log's HTTP endpoints (FR-52).</summary>
public static class IncidentEndpoints
{
    /// <summary>
    /// Maps <c>POST /v1/incidents</c>, <c>GET /v1/incidents/open</c> and <c>POST /v1/incidents/{id}/status</c>. Every route is for
    /// staff with the incident permission. An incident is never something a candidate may see or change, so no route is open to
    /// every signed-in account.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder to map onto.</param>
    public static void MapIncidentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var incidents = endpoints.MapGroup("/v1/incidents")
            .WithTags("Incidents")
            .RequireAuthorization();

        incidents.MapPost("/", LogIncident)
            .RequireAuthorization(IncidentPermissions.Manage)
            .Produces<IncidentDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("LogIncident")
            .WithDescription("Log an incident or breach and start its six-hour escalation timer from the detection time");

        incidents.MapGet("/open", ListOpenIncidents)
            .RequireAuthorization(IncidentPermissions.Manage)
            .Produces<IReadOnlyList<IncidentDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("ListOpenIncidents")
            .WithDescription("List incidents that are not closed, oldest escalation due time first, each with its overdue flag");

        incidents.MapPost("/{incidentId:guid}/status", ChangeIncidentStatus)
            .RequireAuthorization(IncidentPermissions.Manage)
            .Produces<IncidentDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithName("ChangeIncidentStatus")
            .WithDescription("Record a status change on an incident, with a note saying why");
    }

    private static async Task<IResult> LogIncident(
        LogIncidentHttpRequest request,
        HttpContext http,
        LogIncidentHandler handler,
        CancellationToken ct)
    {
        var incident = await handler.HandleAsync(
            new LogIncidentCommand(
                request.Description,
                request.DetectedAtUtc,
                request.Category,
                request.AffectedData,
                http.User.GetUserId()),
            ct);
        return Results.Created($"/v1/incidents/{incident.Id}", incident);
    }

    private static async Task<IResult> ListOpenIncidents(
        int? page,
        int? pageSize,
        ListOpenIncidentsHandler handler,
        CancellationToken ct)
    {
        var incidents = await handler.HandleAsync(PageRequest.Create(page, pageSize), ct);
        return Results.Ok(incidents);
    }

    private static async Task<IResult> ChangeIncidentStatus(
        Guid incidentId,
        ChangeIncidentStatusHttpRequest request,
        HttpContext http,
        ChangeIncidentStatusHandler handler,
        CancellationToken ct)
    {
        var incident = await handler.HandleAsync(
            new ChangeIncidentStatusCommand(incidentId, request.Status, request.Note, http.User.GetUserId()),
            ct);
        return Results.Ok(incident);
    }
}

/// <summary>Request body for <c>POST /v1/incidents</c>. The logging staff member is taken from the caller's token, not the body.</summary>
/// <param name="Description">What happened.</param>
/// <param name="DetectedAtUtc">When it was detected, as a UTC instant.</param>
/// <param name="Category">The kind of incident.</param>
/// <param name="AffectedData">The personal data that may be affected, or that it is not yet known.</param>
public sealed record LogIncidentHttpRequest(
    string Description,
    DateTime DetectedAtUtc,
    IncidentCategory Category,
    string AffectedData);

/// <summary>Request body for <c>POST /v1/incidents/{incidentId}/status</c>. The changing staff member is taken from the token.</summary>
/// <param name="Status">The status to move to.</param>
/// <param name="Note">Why the change is made.</param>
public sealed record ChangeIncidentStatusHttpRequest(IncidentStatus Status, string Note);
