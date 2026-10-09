using System.Security.Claims;
using ExamPlatform.Modules.Invite.Application.Commands;
using ExamPlatform.Modules.Invite.Application.Queries;
using ExamPlatform.SharedKernel.Application.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ExamPlatform.Modules.Invite.Endpoints;

/// <summary>Maps the Invite module's HTTP endpoints (FR-14, FR-50a).</summary>
public static class InviteEndpoints
{
    /// <summary>Maps <c>/v1/invites</c>: staff routes behind <c>invite.manage</c> and the candidate's accept.</summary>
    /// <param name="endpoints">The endpoint route builder to map onto.</param>
    public static void MapInviteEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // The group only demands a signed-in caller. Being signed in says nothing about being allowed to
        // issue invites (a candidate is signed in too), so every staff route also names the permission it needs (FR-2, NFR-5).
        var invites = endpoints.MapGroup("/v1/invites")
            .WithTags("Invite")
            .RequireAuthorization();

        invites.MapPost("/", CreateInvite)
            .RequireAuthorization(InvitePermissions.Manage)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("CreateInvite")
            .WithDescription("Invite an e-mail address to an exam and e-mail it a link");

        invites.MapGet("/", ListInvites)
            .RequireAuthorization(InvitePermissions.Manage)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("ListInvites")
            .WithDescription("List the newest invites");

        invites.MapPost("/{inviteId}/codes", GenerateInviteCode)
            .RequireAuthorization(InvitePermissions.Manage)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("GenerateInviteCode")
            .WithDescription("Generate another single-use code (and its link) for a pending invite, to hand to the invited person; audited");

        // Accepting is the candidate's own action, so it asks for a signed-in caller and nothing more. What
        // keeps it safe is the code and the e-mail check: the caller must hold the invited address.
        invites.MapPost("/accept", AcceptInvite)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithName("AcceptInvite")
            .WithDescription("Accept an invitation with its code; the signed-in account must hold the invited e-mail address");

        // Declining is the invited candidate's action, like accepting, so it asks for a signed-in caller and the
        // invited address. Staff have no reason to decline on a candidate's behalf, so no permission is named.
        invites.MapPost("/{inviteId}/decline", DeclineInvite)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("DeclineInvite")
            .WithDescription("Decline an invitation; the signed-in account must hold the invited e-mail address");

        invites.MapPost("/{inviteId}/revoke", RevokeInvite)
            .RequireAuthorization(InvitePermissions.Manage)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("RevokeInvite")
            .WithDescription("Revoke an invite and all its codes");
    }

    private static async Task<IResult> CreateInvite(
        CreateInviteRequest request,
        ClaimsPrincipal user,
        CreateInviteHandler handler,
        CancellationToken ct)
    {
        // The creator is the authenticated caller, never a value from the body (FR-2, NFR-5).
        var command = new CreateInviteCommand(request.ExamId, request.BatchMemberId, request.Email, user.GetUserId());

        var result = await handler.HandleAsync(command, ct);
        return Results.Created($"/v1/invites/{result.Id}", result);
    }

    private static async Task<IResult> ListInvites(ListInvitesHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(ct));

    private static async Task<IResult> GenerateInviteCode(
        Guid inviteId,
        GenerateInviteCodeRequest request,
        GenerateInviteCodeHandler handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(new GenerateInviteCodeCommand(inviteId, request.ExpiryHours ?? 72), ct);
        return Results.Created($"/v1/invites/{inviteId}/codes", result);
    }

    private static async Task<IResult> AcceptInvite(
        AcceptInviteRequest request,
        ClaimsPrincipal user,
        AcceptInviteHandler handler,
        CancellationToken ct)
    {
        var command = new AcceptInviteCommand(request.Code, user.GetUserId(), user.GetEmail());
        return Results.Ok(await handler.HandleAsync(command, ct));
    }

    private static async Task<IResult> DeclineInvite(Guid inviteId, ClaimsPrincipal user, DeclineInviteHandler handler, CancellationToken ct)
    {
        await handler.HandleAsync(new DeclineInviteCommand(inviteId, user.GetEmail()), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> RevokeInvite(Guid inviteId, RevokeInviteHandler handler, CancellationToken ct)
    {
        await handler.HandleAsync(new RevokeInviteCommand(inviteId), ct);
        return Results.NoContent();
    }
}

/// <summary>Request DTO for inviting an e-mail address to an exam. The inviter is the caller, so it is not part of the body.</summary>
/// <param name="ExamId">The exam the address is invited to.</param>
/// <param name="Email">E-mail address the invitation is sent to.</param>
/// <param name="BatchMemberId">The roster entry it came from, if any.</param>
public record CreateInviteRequest(
    Guid ExamId,
    string Email,
    Guid? BatchMemberId = null);

/// <summary>Request DTO for generating an invite code.</summary>
/// <param name="ExpiryHours">How long the code stays valid, in hours; 72 when omitted.</param>
public record GenerateInviteCodeRequest(
    int? ExpiryHours = null);

/// <summary>Request DTO for accepting an invitation.</summary>
/// <param name="Code">The code from the invitation link.</param>
public record AcceptInviteRequest(
    string? Code);
