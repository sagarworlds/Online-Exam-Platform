using System.Security.Claims;
using ExamPlatform.Modules.Invite.Application.Commands;
using ExamPlatform.Modules.Invite.Application.Dtos;
using ExamPlatform.Modules.Invite.Application.Queries;
using ExamPlatform.Modules.Invite.Domain.Exceptions;
using ExamPlatform.Modules.Invite.Endpoints.RateLimiting;
using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Application.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
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

        invites.MapPost("/change-email", ChangeInviteEmail)
            .RequireAuthorization(InvitePermissions.Manage)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("ChangeInviteEmail")
            .WithDescription("Change the address on every invitation sent to one address, so a student's invitations stay together; audited");

        invites.MapPost("/{inviteId}/revoke", RevokeInvite)
            .RequireAuthorization(InvitePermissions.Manage)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("RevokeInvite")
            .WithDescription("Revoke an invite and all its codes");

        // Outside the group above, which needs a signed-in caller: nobody is signed in yet, and the one-time code in the link is the only
        // proof. It signs the invited student in, so it is rate limited per client and only a code made for it is accepted.
        endpoints.MapPost("/v1/invites/direct-sign-in", DirectSignIn)
            .AllowAnonymous()
            .RequireRateLimiting(InviteRateLimitPolicies.DirectSignIn)
            .WithTags("Invite")
            .Produces<DirectSignInResultDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status429TooManyRequests)
            .WithName("DirectSignIn")
            .WithDescription("Sign the invited student in with the code of a direct sign-in link, accept the invitation, and name the exam to open");
    }

    private static async Task<IResult> CreateInvite(
        CreateInviteRequest request,
        ClaimsPrincipal user,
        CreateInviteHandler handler,
        CancellationToken ct)
    {
        EnsureMayMakeDirectLink(user, request.DirectSignIn);

        // The creator is the authenticated caller, never a value from the body (FR-2, NFR-5).
        var command = new CreateInviteCommand(request.ExamId, request.BatchMemberId, request.Email, user.GetUserId(), request.DirectSignIn);

        var result = await handler.HandleAsync(command, ct);
        return Results.Created($"/v1/invites/{result.Id}", result);
    }

    private static async Task<IResult> ListInvites(ListInvitesHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(ct));

    private static async Task<IResult> GenerateInviteCode(
        Guid inviteId,
        GenerateInviteCodeRequest request,
        ClaimsPrincipal user,
        GenerateInviteCodeHandler handler,
        CancellationToken ct)
    {
        EnsureMayMakeDirectLink(user, request.DirectSignIn);

        var result = await handler.HandleAsync(new GenerateInviteCodeCommand(inviteId, request.ExpiryHours ?? 72, request.DirectSignIn), ct);
        return Results.Created($"/v1/invites/{inviteId}/codes", result);
    }

    // A direct link signs in as whoever owns the invited address, so holding invite.manage is not enough to make one: it is the
    // permission that stops a role which only manages invitations from signing in as any student.
    private static void EnsureMayMakeDirectLink(ClaimsPrincipal user, bool directSignIn)
    {
        if (directSignIn && !user.HasPermission(InvitePermissions.DirectLinkCode))
            throw new DirectLinkNotPermittedError();
    }

    private static async Task<IResult> DirectSignIn(DirectSignInRequest request, IClientInfo client, DirectSignInHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(new DirectSignInCommand(request.Code, client.DeviceFingerprint, client.IpAddress), ct));

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

    private static async Task<IResult> ChangeInviteEmail(ChangeInviteEmailRequest request, ChangeInviteEmailHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(new ChangeInviteEmailCommand(request.CurrentEmail, request.NewEmail), ct));

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
/// <param name="DirectSignIn">
/// Whether the link sent to the invited person signs them in and takes them to the exam. It needs the <c>invite.direct-link</c>
/// permission; leaving it out gives the ordinary invitation.
/// </param>
public record CreateInviteRequest(
    Guid ExamId,
    string Email,
    Guid? BatchMemberId = null,
    bool DirectSignIn = false);

/// <summary>Request DTO for generating an invite code.</summary>
/// <param name="ExpiryHours">How long the code stays valid, in hours; 72 when omitted.</param>
/// <param name="DirectSignIn">Whether the code's link signs the invited person in; it needs the <c>invite.direct-link</c> permission.</param>
public record GenerateInviteCodeRequest(
    int? ExpiryHours = null,
    bool DirectSignIn = false);

/// <summary>Request DTO for opening a direct sign-in link.</summary>
/// <param name="Code">The code the link carries.</param>
public record DirectSignInRequest(
    string? Code);

/// <summary>Request DTO for accepting an invitation.</summary>
/// <param name="Code">The code from the invitation link.</param>
public record AcceptInviteRequest(
    string? Code);

/// <summary>Request DTO for changing the address on a student's invitations.</summary>
/// <param name="CurrentEmail">The address the invitations were sent to.</param>
/// <param name="NewEmail">The address they are to have.</param>
public record ChangeInviteEmailRequest(
    string? CurrentEmail,
    string? NewEmail);
