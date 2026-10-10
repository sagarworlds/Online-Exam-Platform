using ExamPlatform.Modules.Guardian.Application.Commands;
using ExamPlatform.Modules.Guardian.Application.Dtos;
using ExamPlatform.Modules.Guardian.Application.Queries;
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

        // Staff look a guardian up by the address they know, to link a candidate to them (the id is not something staff have).
        guardians.MapGet("/", FindGuardianByEmail)
            .RequireAuthorization(GuardianPermissions.LinkManage)
            .Produces<GuardianDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithName("FindGuardianByEmail")
            .WithDescription("Find the guardian registered with an e-mail address");

        guardians.MapGet("/{guardianId}/links", ListGuardianLinks)
            .RequireAuthorization(GuardianPermissions.LinkManage)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("ListGuardianLinks")
            .WithDescription("List a guardian's candidate links, revoked ones included");

        // Outside the guardians group, which needs a signed-in caller: a guardian has no account to sign in with, so the one-time
        // code in the e-mail is the only proof. Confirming a link is the only thing this route does.
        endpoints.MapPost("/v1/guardian-links/verify", VerifyGuardianLink)
            .AllowAnonymous()
            .WithTags("Guardian")
            .Produces<GuardianLinkDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status409Conflict)
            .WithName("VerifyGuardianLink")
            .WithDescription("Confirm a candidate link with the one-time code e-mailed to the guardian");
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

    private static async Task<IResult> FindGuardianByEmail(
        string? email,
        FindGuardianByEmailHandler handler,
        CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(email, ct));

    private static async Task<IResult> ListGuardianLinks(
        Guid guardianId,
        ListGuardianLinksHandler handler,
        CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(guardianId, ct));

    private static async Task<IResult> VerifyGuardianLink(
        VerifyGuardianLinkRequest request,
        VerifyGuardianLinkHandler handler,
        CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(new VerifyGuardianLinkCommand(request.Token), ct));
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

/// <summary>Request DTO for confirming a candidate link with the code e-mailed to the guardian.</summary>
public record VerifyGuardianLinkRequest(string? Token);
