using System.IdentityModel.Tokens.Jwt;
using ExamPlatform.Modules.Identity.Application.Sessions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace ExamPlatform.Modules.Identity.Endpoints.Authentication;

/// <summary>
/// JWT bearer events that make a token only as good as its session (FR-4): once a token's
/// signature and lifetime check out, its <c>sid</c> is checked against the stored session,
/// so a superseded, logged-out or expired session, or one whose account is locked, is
/// refused on the very next request. A refused request gets a 401 whose problem title is the
/// stable reason code (e.g. <c>session_superseded</c>), which the web app reads to explain
/// why the user was signed out.
/// Resolved from the request's services on every request (see
/// <see cref="Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions.EventsType"/>), so it can use scoped, per-request services.
/// </summary>
public sealed class SessionValidatingJwtBearerEvents(
    SessionValidator sessionValidator,
    ILogger<SessionValidatingJwtBearerEvents> logger) : JwtBearerEvents
{
    // The JWT claim that names the session; written by Identity.Infrastructure's JwtTokenGenerator.
    private const string SessionIdClaim = "sid";

    // Carries the rejection from TokenValidated to Challenge, which runs later in the same request.
    private static readonly object RejectionItemKey = new();

    /// <inheritdoc />
    public override async Task TokenValidated(TokenValidatedContext context)
    {
        // Raw claim names: the Host turns off inbound claim mapping (MapInboundClaims = false).
        var principal = context.Principal;
        var hasUserId = Guid.TryParse(principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var userId);
        var hasSessionId = Guid.TryParse(principal?.FindFirst(SessionIdClaim)?.Value, out var sessionId);

        var result = hasUserId && hasSessionId
            ? await sessionValidator.ValidateAsync(userId, sessionId, context.HttpContext.RequestAborted)
            : SessionValidationResult.Unknown;

        if (result == SessionValidationResult.Valid)
        {
            return;
        }

        // Information, not Warning: a superseded or expired session is routine (a second
        // login, a tab left open), and ids only, never the token or contact details.
        logger.LogInformation(
            "Rejected access token for user {UserId}, session {SessionId}: {Reason}",
            hasUserId ? userId : (Guid?)null,
            hasSessionId ? sessionId : (Guid?)null,
            result);

        context.HttpContext.Items[RejectionItemKey] = result;
        context.Fail(result.ToErrorCode());
    }

    /// <inheritdoc />
    public override async Task Challenge(JwtBearerChallengeContext context)
    {
        var rejection = context.HttpContext.Items.TryGetValue(RejectionItemKey, out var stashed)
            ? (SessionValidationResult?)stashed
            : null;

        // A token that is past its own expiry fails the bearer handler's lifetime check before
        // TokenValidated runs; the token's expiry is the session's, so it is reported as the
        // same session_expired a client sees for a session that expired inside the clock skew.
        if (rejection is null && context.AuthenticateFailure is SecurityTokenExpiredException)
        {
            rejection = SessionValidationResult.Expired;
        }

        if (rejection is not { } result)
        {
            await base.Challenge(context);
            return;
        }

        context.HandleResponse();
        var response = context.Response;
        response.StatusCode = StatusCodes.Status401Unauthorized;
        response.Headers.WWWAuthenticate = "Bearer error=\"invalid_token\"";
        await response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = result.ToErrorCode(),
                Detail = result.ToMessage(),
                Instance = context.Request.Path,
            },
            context.HttpContext.RequestAborted);
    }
}
