using ExamPlatform.Modules.Guardian.Application.Commands;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ExamPlatform.Modules.Guardian.Endpoints;

/// <summary>Maps the Guardian module's HTTP endpoints (FR-22, FR-23, FR-24).</summary>
public static class GuardianEndpoints
{
    /// <summary>Maps <c>POST /v1/guardians</c> and other guardian management endpoints.</summary>
    /// <param name="endpoints">The endpoint route builder to map onto.</param>
    public static void MapGuardianEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // The group only demands a signed-in caller. Guardian records and links are staff-managed for now:
        // nothing yet binds a guardian record to a guardian's own account, so a route open to every
        // signed-in user could create or sever links for anyone. Each staff route therefore names the
        // permission it needs (FR-2, NFR-5). Guardian accounts and self-service are redesigned in a later change.
        var guardians = endpoints.MapGroup("/v1/guardians")
            .WithTags("Guardian")
            .RequireAuthorization();

        guardians.MapPost("/", CreateGuardian)
            .RequireAuthorization(GuardianPermissions.LinkManage)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("CreateGuardian")
            .WithDescription("Register a new guardian");

        guardians.MapPost("/{guardianId}/links", LinkCandidate)
            .RequireAuthorization(GuardianPermissions.LinkManage)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("LinkCandidate")
            .WithDescription("Link a guardian to a candidate");

        guardians.MapDelete("/{guardianId}/links/{candidateId}", RevokeGuardianLink)
            .RequireAuthorization(GuardianPermissions.LinkManage)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("RevokeGuardianLink")
            .WithDescription("Revoke a guardian link to a candidate");

        guardians.MapDelete("/{guardianId}/candidates/{candidateId}", UnlinkCandidate)
            .RequireAuthorization(GuardianPermissions.LinkManage)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("UnlinkCandidate")
            .WithDescription("Unlink a candidate from a guardian");
    }

    private static async Task<IResult> CreateGuardian(
        CreateGuardianRequest request,
        CreateGuardianHandler handler,
        CancellationToken ct)
    {
        var command = new CreateGuardianCommand(
            request.Email,
            request.FullName,
            request.Phone);

        var result = await handler.HandleAsync(command, ct);
        return Results.Created($"/v1/guardians/{result.Id}", result);
    }

    private static async Task<IResult> LinkCandidate(
        Guid guardianId,
        LinkCandidateRequest request,
        LinkCandidateHandler handler,
        CancellationToken ct)
    {
        var command = new LinkCandidateCommand(
            guardianId,
            request.CandidateId,
            request.CandidateEmail);

        var result = await handler.HandleAsync(command, ct);
        return Results.Created($"/v1/guardians/{guardianId}/links", result);
    }

    private static async Task<IResult> RevokeGuardianLink(
        Guid guardianId,
        Guid candidateId,
        RevokeGuardianLinkHandler handler,
        CancellationToken ct)
    {
        var command = new RevokeGuardianLinkCommand(guardianId, candidateId);
        await handler.HandleAsync(command, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> UnlinkCandidate(
        Guid guardianId,
        Guid candidateId,
        UnlinkCandidateHandler handler,
        CancellationToken ct)
    {
        var command = new UnlinkCandidateCommand(guardianId, candidateId);
        await handler.HandleAsync(command, ct);
        return Results.NoContent();
    }
}

/// <summary>Request DTO for creating a guardian.</summary>
public record CreateGuardianRequest(
    string Email,
    string FullName,
    string? Phone = null);

/// <summary>Request DTO for linking a guardian to a candidate.</summary>
public record LinkCandidateRequest(
    Guid CandidateId,
    string CandidateEmail);
