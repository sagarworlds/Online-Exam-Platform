using ExamPlatform.Modules.Identity.Application.Commands;
using ExamPlatform.Modules.Identity.Application.Queries;
using ExamPlatform.Modules.Identity.Domain.Rbac;
using ExamPlatform.Modules.Identity.Endpoints.RateLimiting;
using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Application.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;

namespace ExamPlatform.Modules.Identity.Endpoints;

/// <summary>Maps the Identity module's HTTP endpoints (FR-1, FR-2, FR-3, FR-4).</summary>
public static class IdentityEndpoints
{
    /// <summary>Maps <c>/v1/auth/*</c>, <c>/v1/me/*</c>, and the role-assignment admin endpoints.</summary>
    /// <param name="endpoints">The endpoint route builder to map onto.</param>
    public static void MapIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapDataRequestEndpoints();

        var auth = endpoints.MapGroup("/v1/auth").WithTags("Identity");

        auth.MapPost("/otp/request", async (RequestOtpRequest request, RequestOtpHandler handler, CancellationToken ct) =>
        {
            var channel = OtpChannelParser.Parse(request.Channel);
            var challengeId = await handler.HandleAsync(new RequestOtpCommand(channel, request.Destination), ct);
            return Results.Ok(new { otpChallengeId = challengeId });
        })
            .RequireRateLimiting(IdentityRateLimitPolicies.OtpRequest);

        auth.MapPost("/otp/verify", async (VerifyOtpRequest request, HttpContext http, VerifyOtpHandler handler, CancellationToken ct) =>
        {
            var command = new VerifyOtpCommand(
                request.OtpChallengeId, request.Code, DeviceFingerprint(http), ClientIp(http));
            var result = await handler.HandleAsync(command, ct);
            return Results.Ok(result);
        })
            .RequireRateLimiting(IdentityRateLimitPolicies.OtpVerify);

        auth.MapPost("/register", async (RegisterCandidateRequest request, RegisterCandidateHandler handler, CancellationToken ct) =>
        {
            var channel = OtpChannelParser.Parse(request.OtpChannel);
            var command = new RegisterCandidateCommand(
                request.Email, request.PhoneNumber, request.DateOfBirth, request.DisplayName, channel);
            var challengeId = await handler.HandleAsync(command, ct);
            return Results.Ok(new { otpChallengeId = challengeId });
        })
            .RequireRateLimiting(IdentityRateLimitPolicies.OtpRequest);

        auth.MapPost("/login", async (PasswordLoginRequest request, HttpContext http, PasswordLoginHandler handler, CancellationToken ct) =>
        {
            var command = new PasswordLoginCommand(request.Email, request.Password, DeviceFingerprint(http), ClientIp(http));
            var result = await handler.HandleAsync(command, ct);
            return Results.Ok(result);
        })
            .RequireRateLimiting(IdentityRateLimitPolicies.PasswordLogin);

        auth.MapPost("/password-reset/request", async (RequestPasswordResetRequest request, RequestPasswordResetHandler handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new RequestPasswordResetCommand(request.Email), ct);
            return Results.Ok();
        })
            .RequireRateLimiting(IdentityRateLimitPolicies.PasswordReset);

        auth.MapPost("/password-reset/reset", async (ResetPasswordRequest request, ResetPasswordHandler handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(
                new ResetPasswordCommand(request.PasswordResetTokenId, request.Token, request.NewPassword), ct);
            return Results.Ok();
        })
            .RequireRateLimiting(IdentityRateLimitPolicies.PasswordReset);

        auth.MapPost("/logout", async (HttpContext http, LogoutHandler handler, CancellationToken ct) =>
            {
                await handler.HandleAsync(new LogoutCommand(http.User.GetUserId(), http.User.GetSessionId()), ct);
                return Results.NoContent();
            })
            .RequireAuthorization();

        var me = endpoints.MapGroup("/v1/me").WithTags("Identity").RequireAuthorization();

        me.MapGet("/profile", async (HttpContext http, GetProfileHandler handler, CancellationToken ct) =>
        {
            var profile = await handler.HandleAsync(new GetProfileQuery(http.User.GetUserId()), ct);
            return Results.Ok(profile);
        });

        me.MapPut("/profile", async (UpdateProfileRequest request, HttpContext http, UpdateProfileHandler handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new UpdateProfileCommand(http.User.GetUserId(), request.DisplayName), ct);
            return Results.NoContent();
        });

        endpoints.MapPost("/v1/admin/users/{userId:guid}/roles", async (
                Guid userId, AssignRoleRequest request, HttpContext http, AssignRoleHandler handler, CancellationToken ct) =>
            {
                var command = new AssignRoleCommand(userId, request.RoleId, http.User.GetUserId(), http.User.GetPrimaryRole());
                await handler.HandleAsync(command, ct);
                return Results.NoContent();
            })
            .WithTags("Identity")
            .RequireAuthorization("permission:identity.role.assign");

        // The same permission as assigning a role: the list exists so an administrator can find the
        // id that route takes, and anyone who may assign roles may see what there is to assign.
        endpoints.MapGet("/v1/admin/roles", async (ListRolesHandler handler, CancellationToken ct) =>
            {
                var roles = await handler.HandleAsync(ct);
                return Results.Ok(roles);
            })
            .WithTags("Identity")
            .RequireAuthorization("permission:identity.role.assign");

        // Reveals live candidate codes, so it has its own permission, held by SuperAdmin only, and
        // every call is audited by the handler.
        endpoints.MapGet("/v1/admin/otp-codes", async (
                string? destination, HttpContext http, ListOutstandingOtpsHandler handler, CancellationToken ct) =>
            {
                var query = new ListOutstandingOtpsQuery(destination, http.User.GetUserId(), http.User.GetPrimaryRole());
                return Results.Ok(await handler.HandleAsync(query, ct));
            })
            .WithTags("Identity")
            .RequireAuthorization("permission:identity.otp.read");

        // The administrator's WhatsApp test: what is configured, a real message sent through the real connection, and what became of it.
        // It sends to real numbers, so it has its own permission, held by SuperAdmin only, and every send is audited.
        var whatsApp = endpoints.MapGroup("/v1/admin/whatsapp")
            .WithTags("Identity")
            .RequireAuthorization($"permission:{RbacCatalog.PermissionCodes.WhatsAppTest}");

        whatsApp.MapGet("/status", (GetWhatsAppStatusHandler handler) => Results.Ok(handler.Handle()))
            .WithName("GetWhatsAppStatus")
            .WithDescription("Review the WhatsApp settings (no secret is shown) and say what is missing");

        whatsApp.MapPost("/messages", async (
                SendWhatsAppTestRequest request, HttpContext http, SendWhatsAppTestHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(
                new SendWhatsAppTestCommand(request.PhoneNumber, request.Mode, request.Message, http.User.GetUserId(), http.User.GetPrimaryRole()), ct)))
            .WithName("SendWhatsAppTestMessage")
            .WithDescription("Send a test message over WhatsApp; if it cannot be sent, the answer says exactly why");

        whatsApp.MapGet("/messages/{messageId}", (string messageId, GetWhatsAppDeliveryHandler handler) => Results.Ok(handler.Handle(messageId)))
            .WithName("GetWhatsAppDelivery")
            .WithDescription("What Meta has reported about a test message: sent, delivered, read or failed, and why");
    }

    // Cleaned the same way for every flow, so a login cannot store more than an attempt would (FR-26).
    private static string? DeviceFingerprint(HttpContext http) =>
        http.Request.Headers.TryGetValue(ClientInfo.FingerprintHeader, out var value) ? ClientInfo.CleanFingerprint(value.ToString()) : null;

    private static string? ClientIp(HttpContext http) => ClientInfo.CleanIp(http.Connection.RemoteIpAddress?.ToString());
}
