using ExamPlatform.Modules.Identity.Application.Commands;
using ExamPlatform.Modules.Identity.Application.Queries;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ExamPlatform.Modules.Identity.Endpoints;

/// <summary>Maps the Identity module's HTTP endpoints (FR-1, FR-2, FR-3, FR-4).</summary>
public static class IdentityEndpoints
{
    /// <summary>Maps <c>/v1/auth/*</c>, <c>/v1/me/*</c>, and the role-assignment admin endpoint.</summary>
    /// <param name="endpoints">The endpoint route builder to map onto.</param>
    public static void MapIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var auth = endpoints.MapGroup("/v1/auth").WithTags("Identity");

        auth.MapPost("/otp/request", async (RequestOtpRequest request, RequestOtpHandler handler, CancellationToken ct) =>
        {
            var channel = OtpChannelParser.Parse(request.Channel);
            var challengeId = await handler.HandleAsync(new RequestOtpCommand(channel, request.Destination), ct);
            return Results.Ok(new { otpChallengeId = challengeId });
        });

        auth.MapPost("/otp/verify", async (VerifyOtpRequest request, HttpContext http, VerifyOtpHandler handler, CancellationToken ct) =>
        {
            var command = new VerifyOtpCommand(
                request.OtpChallengeId, request.Code, DeviceFingerprint(http), ClientIp(http));
            var result = await handler.HandleAsync(command, ct);
            return Results.Ok(result);
        });

        auth.MapPost("/register", async (RegisterCandidateRequest request, RegisterCandidateHandler handler, CancellationToken ct) =>
        {
            var channel = OtpChannelParser.Parse(request.OtpChannel);
            var command = new RegisterCandidateCommand(
                request.Email, request.PhoneNumber, request.DateOfBirth, request.DisplayName, channel);
            var challengeId = await handler.HandleAsync(command, ct);
            return Results.Ok(new { otpChallengeId = challengeId });
        });

        auth.MapPost("/login", async (PasswordLoginRequest request, HttpContext http, PasswordLoginHandler handler, CancellationToken ct) =>
        {
            var command = new PasswordLoginCommand(request.Email, request.Password, DeviceFingerprint(http), ClientIp(http));
            var result = await handler.HandleAsync(command, ct);
            return Results.Ok(result);
        });

        auth.MapPost("/password-reset/request", async (RequestPasswordResetRequest request, RequestPasswordResetHandler handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new RequestPasswordResetCommand(request.Email), ct);
            return Results.Ok();
        });

        auth.MapPost("/password-reset/reset", async (ResetPasswordRequest request, ResetPasswordHandler handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(
                new ResetPasswordCommand(request.PasswordResetTokenId, request.Token, request.NewPassword), ct);
            return Results.Ok();
        });

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
    }

    private static string? DeviceFingerprint(HttpContext http) =>
        http.Request.Headers.TryGetValue("X-Device-Fingerprint", out var value) ? value.ToString() : null;

    private static string? ClientIp(HttpContext http) => http.Connection.RemoteIpAddress?.ToString();
}
