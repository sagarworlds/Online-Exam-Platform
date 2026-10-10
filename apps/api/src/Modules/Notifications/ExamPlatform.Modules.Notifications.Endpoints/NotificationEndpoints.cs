using ExamPlatform.Modules.Notifications.Application.Commands;
using ExamPlatform.Modules.Notifications.Application.Queries;
using ExamPlatform.SharedKernel.Application.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ExamPlatform.Modules.Notifications.Endpoints;

/// <summary>
/// Maps the signed-in account's own in-app feed (FR-39). Every route acts on the caller's notices only, taken from the token, so there is
/// no route that names another account. Any signed-in user may call them; no permission is needed to read one's own feed.
/// </summary>
public static class NotificationEndpoints
{
    /// <summary>Maps <c>GET /v1/me/notifications</c>, <c>GET .../unread-count</c>, <c>POST .../{id}/read</c> and <c>POST .../read-all</c>.</summary>
    /// <param name="endpoints">The endpoint route builder to map onto.</param>
    public static void MapNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/v1/me/notifications").WithTags("Notifications").RequireAuthorization();

        group.MapGet("/", async (int? page, int? pageSize, HttpContext http, ListMyNotificationsHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(http.User.GetUserId(), page, pageSize, ct)));

        group.MapGet("/unread-count", async (HttpContext http, CountUnreadNotificationsHandler handler, CancellationToken ct) =>
            Results.Ok(new UnreadCountResponse(await handler.HandleAsync(http.User.GetUserId(), ct))));

        group.MapPost("/{notificationId:guid}/read", async (Guid notificationId, HttpContext http, MarkNotificationReadHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(notificationId, http.User.GetUserId(), ct)));

        group.MapPost("/read-all", async (HttpContext http, MarkAllNotificationsReadHandler handler, CancellationToken ct) =>
            Results.Ok(new MarkedReadResponse(await handler.HandleAsync(http.User.GetUserId(), ct))));
    }
}

/// <summary>Body of <c>GET /v1/me/notifications/unread-count</c>.</summary>
/// <param name="UnreadCount">How many of the caller's notices are unread.</param>
public sealed record UnreadCountResponse(int UnreadCount);

/// <summary>Body of <c>POST /v1/me/notifications/read-all</c>.</summary>
/// <param name="Marked">How many notices were unread and are now read.</param>
public sealed record MarkedReadResponse(int Marked);
