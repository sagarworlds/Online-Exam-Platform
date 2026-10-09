using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace ExamPlatform.Modules.ExamRuntime.Endpoints.Notifications;

/// <summary>
/// The route an outside scheduler calls to start a notification pass (FR-39), for a host that sleeps when idle and so cannot trust its
/// own timer. Anonymous by nature (a scheduler holds no account), so each call is proven instead, by the run key in a header. Until a
/// key is configured the route answers 404, as if not there, like the WhatsApp webhook does.
/// </summary>
internal static class NotificationEndpoints
{
    /// <summary>The route the scheduler calls.</summary>
    public const string Route = "/v1/notifications/run";

    /// <summary>The header that carries the run key.</summary>
    public const string KeyHeader = "X-Notifications-Key";

    /// <summary>Maps the route.</summary>
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(Route, RunAsync).AllowAnonymous().ExcludeFromDescription();
        return endpoints;
    }

    private static async Task<IResult> RunAsync(HttpRequest request, IOptions<NotificationOptions> options, NotificationRunner runner, CancellationToken cancellationToken)
    {
        var key = options.Value.RunKey;
        if (string.IsNullOrEmpty(key))
            return Results.NotFound();

        if (!KeyMatches(request.Headers[KeyHeader].ToString(), key))
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        var summary = await runner.RunOnceAsync(cancellationToken);

        // Another pass was already running: it has this one's work in hand, which is as good as having done it.
        return summary is null ? Results.Accepted() : Results.Ok(summary);
    }

    // Compared as hashes, so the comparison takes the same time for any guess and the key's length is not learned from how long it took.
    private static bool KeyMatches(string supplied, string expected) =>
        CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(supplied)), SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
}
