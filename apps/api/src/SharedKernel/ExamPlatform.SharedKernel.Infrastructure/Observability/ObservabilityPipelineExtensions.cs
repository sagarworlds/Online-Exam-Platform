using ExamPlatform.SharedKernel.Infrastructure.Health;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Routing;

namespace ExamPlatform.SharedKernel.Infrastructure.Observability;

/// <summary>Adds the correlation and request-logging middleware, and the liveness and readiness endpoints (NFR-3, NFR-9).</summary>
public static class ObservabilityPipelineExtensions
{
    /// <summary>The liveness route: answers while the process is serving requests, and checks nothing else.</summary>
    public const string LivenessPath = "/v1/health/live";

    /// <summary>The readiness route: answers only when the checks tagged <see cref="DatabaseReadinessCheck.ReadyTag"/> pass.</summary>
    public const string ReadinessPath = "/v1/health/ready";

    /// <summary>
    /// Adds the correlation-id middleware and then the request-logging middleware. Their order matters: the correlation id must be
    /// in place before the request line is written, so that the line carries it.
    /// </summary>
    /// <param name="app">The application pipeline.</param>
    /// <returns>The same pipeline, for chaining.</returns>
    public static IApplicationBuilder UseExamPlatformRequestObservability(this IApplicationBuilder app)
    {
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseMiddleware<RequestLoggingMiddleware>();
        return app;
    }

    /// <summary>
    /// Maps the liveness route, which runs no checks, and the readiness route, which runs the database check. The original
    /// <c>/v1/health</c> route is mapped by the Host and is left as it is: Render's health check and the web client both use it.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapExamPlatformHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks(LivenessPath, new HealthCheckOptions { Predicate = _ => false });
        endpoints.MapHealthChecks(
            ReadinessPath,
            new HealthCheckOptions { Predicate = check => check.Tags.Contains(DatabaseReadinessCheck.ReadyTag) });
        return endpoints;
    }
}
