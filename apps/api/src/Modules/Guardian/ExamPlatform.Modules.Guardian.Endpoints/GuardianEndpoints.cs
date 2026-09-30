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
        var guardians = endpoints.MapGroup("/v1/guardians").WithTags("Guardian").RequireAuthorization();

        guardians.MapPost("/", CreateGuardian)
            .WithName("CreateGuardian")
            .WithDescription("Register a new guardian");

        guardians.MapPost("/{guardianId}/links", LinkCandidate)
            .WithName("LinkCandidate")
            .WithDescription("Link a guardian to a candidate");

        guardians.MapPost("/links/verify", VerifyGuardianLink)
            .WithName("VerifyGuardianLink")
            .WithDescription("Verify and confirm a guardian link");

        guardians.MapDelete("/{guardianId}/links/{candidateId}", RevokeGuardianLink)
            .WithName("RevokeGuardianLink")
            .WithDescription("Revoke a guardian link to a candidate");

        guardians.MapDelete("/{guardianId}/candidates/{candidateId}", UnlinkCandidate)
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

        var result = await handler.Handle(command, ct);
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

        var result = await handler.Handle(command, ct);
        return Results.Created($"/v1/guardians/{guardianId}/links", result);
    }

    private static async Task<IResult> VerifyGuardianLink(
        VerifyGuardianLinkRequest request,
        VerifyGuardianLinkHandler handler,
        CancellationToken ct)
    {
        var command = new VerifyGuardianLinkCommand(request.VerificationToken);
        await handler.Handle(command, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> RevokeGuardianLink(
        Guid guardianId,
        Guid candidateId,
        RevokeGuardianLinkHandler handler,
        CancellationToken ct)
    {
        var command = new RevokeGuardianLinkCommand(guardianId, candidateId);
        await handler.Handle(command, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> UnlinkCandidate(
        Guid guardianId,
        Guid candidateId,
        UnlinkCandidateHandler handler,
        CancellationToken ct)
    {
        var command = new UnlinkCandidateCommand(guardianId, candidateId);
        await handler.Handle(command, ct);
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

/// <summary>Request DTO for verifying a guardian link.</summary>
public record VerifyGuardianLinkRequest(
    string VerificationToken);
