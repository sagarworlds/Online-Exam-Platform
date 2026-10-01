using ExamPlatform.Modules.Invite.Application.Commands;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ExamPlatform.Modules.Invite.Endpoints;

/// <summary>Maps the Invite module's HTTP endpoints (FR-20, FR-21).</summary>
public static class InviteEndpoints
{
    /// <summary>Maps <c>POST /v1/invites</c> and other invite management endpoints.</summary>
    /// <param name="endpoints">The endpoint route builder to map onto.</param>
    public static void MapInviteEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var invites = endpoints.MapGroup("/v1/invites").WithTags("Invite").RequireAuthorization();

        invites.MapPost("/", CreateInvite)
            .WithName("CreateInvite")
            .WithDescription("Create a new invite");

        invites.MapPost("/{inviteId}/codes", GenerateInviteCode)
            .WithName("GenerateInviteCode")
            .WithDescription("Generate an invite code");

        invites.MapPost("/{inviteId}/accept", AcceptInvite)
            .WithName("AcceptInvite")
            .WithDescription("Accept an invite with a code");

        invites.MapPost("/{inviteId}/decline", DeclineInvite)
            .WithName("DeclineInvite")
            .WithDescription("Decline an invite");

        invites.MapPost("/{inviteId}/revoke", RevokeInvite)
            .WithName("RevokeInvite")
            .WithDescription("Revoke an invite and all its codes");
    }

    private static async Task<IResult> CreateInvite(
        CreateInviteRequest request,
        CreateInviteHandler handler,
        CancellationToken ct)
    {
        var command = new CreateInviteCommand(
            request.ExamId,
            request.BatchMemberId,
            request.Email,
            request.CreatedByUserId);

        var result = await handler.HandleAsync(command, ct);
        return Results.Created($"/v1/invites/{result.Id}", result);
    }

    private static async Task<IResult> GenerateInviteCode(
        Guid inviteId,
        GenerateInviteCodeRequest request,
        GenerateInviteCodeHandler handler,
        CancellationToken ct)
    {
        var command = new GenerateInviteCodeCommand(
            inviteId,
            request.ExpiryHours ?? 72);

        var result = await handler.HandleAsync(command, ct);
        return Results.Created($"/v1/invites/{inviteId}/codes", result);
    }

    private static async Task<IResult> AcceptInvite(
        Guid inviteId,
        AcceptInviteRequest request,
        AcceptInviteHandler handler,
        CancellationToken ct)
    {
        var command = new AcceptInviteCommand(
            inviteId,
            request.InviteCodeId);

        await handler.HandleAsync(command, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> DeclineInvite(
        Guid inviteId,
        DeclineInviteHandler handler,
        CancellationToken ct)
    {
        var command = new DeclineInviteCommand(inviteId);
        await handler.HandleAsync(command, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> RevokeInvite(
        Guid inviteId,
        RevokeInviteHandler handler,
        CancellationToken ct)
    {
        var command = new RevokeInviteCommand(inviteId);
        await handler.HandleAsync(command, ct);
        return Results.NoContent();
    }
}

/// <summary>Request DTO for creating an invite.</summary>
public record CreateInviteRequest(
    Guid ExamId,
    Guid BatchMemberId,
    string Email,
    Guid CreatedByUserId);

/// <summary>Request DTO for generating an invite code.</summary>
public record GenerateInviteCodeRequest(
    int? ExpiryHours = null);

/// <summary>Request DTO for accepting an invite.</summary>
public record AcceptInviteRequest(
    Guid InviteCodeId);
