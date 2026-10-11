using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace ExamPlatform.SharedKernel.Infrastructure.Observability;

/// <summary>
/// Writes one log line per request with its outcome: method, route, status and duration (NFR-9). Runs outside the rest of the
/// pipeline, so the line also covers rate-limit rejections and errors the exception handler turned into responses.
/// </summary>
/// <remarks>
/// What the line leaves out is deliberate. It names the route <em>template</em>, not the request path, because a path can carry a
/// value a caller meant to keep private. It never includes the query string (the WhatsApp webhook's verify token travels there),
/// the headers, or the body. An unmatched request is labelled <see cref="NoRouteLabel"/>, not its raw path, for the same reason.
/// </remarks>
/// <param name="next">The next middleware in the pipeline.</param>
/// <param name="logger">Writes the request line.</param>
public sealed class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    /// <summary>What the log says for a request no endpoint matched, or matched without a route template.</summary>
    public const string NoRouteLabel = "(no route)";

    /// <summary>
    /// Route templates under this prefix are the health probes, which an orchestrator sends every few seconds. Their routine
    /// answers are logged at Debug so they do not drown the rest of the log.
    /// </summary>
    public const string HealthRoutePrefix = "/v1/health";

    /// <summary>Runs the rest of the pipeline and then writes the request's outcome.</summary>
    /// <param name="context">The current request.</param>
    /// <returns>A task that completes when the request has been answered.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        var started = Stopwatch.GetTimestamp();
        var completed = false;
        try
        {
            await next(context);
            completed = true;
        }
        finally
        {
            // An exception that escaped the pipeline is answered with a 500 by the host, so the line says so.
            var status = completed ? context.Response.StatusCode : StatusCodes.Status500InternalServerError;
            Write(context, status, Stopwatch.GetElapsedTime(started));
        }
    }

    /// <summary>The level a request's outcome is logged at.</summary>
    /// <param name="statusCode">The HTTP status the request was answered with.</param>
    /// <param name="route">The route template, or <see cref="NoRouteLabel"/>.</param>
    /// <returns>Error for a 5xx; Warning for the statuses that signal an attack or a lock-out (401, 403, 429); Debug for a healthy
    /// probe; Information otherwise.</returns>
    public static LogLevel LevelFor(int statusCode, string route)
    {
        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            return LogLevel.Error;
        }

        if (route.StartsWith(HealthRoutePrefix, StringComparison.Ordinal))
        {
            return LogLevel.Debug;
        }

        return statusCode is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden or StatusCodes.Status429TooManyRequests
            ? LogLevel.Warning
            : LogLevel.Information;
    }

    /// <summary>The route template of the endpoint that answered, or <see cref="NoRouteLabel"/>.</summary>
    /// <param name="context">The request, after routing.</param>
    /// <returns>The template (for example <c>/v1/batches/{batchId}</c>), never the values filled into it.</returns>
    public static string RouteOf(HttpContext context) =>
        (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? NoRouteLabel;

    private void Write(HttpContext context, int status, TimeSpan elapsed)
    {
        var route = RouteOf(context);
        logger.Log(
            LevelFor(status, route),
            "HTTP {Method} {Route} responded {StatusCode} in {ElapsedMs} ms",
            context.Request.Method,
            route,
            status,
            Math.Round(elapsed.TotalMilliseconds, 1));
    }
}
