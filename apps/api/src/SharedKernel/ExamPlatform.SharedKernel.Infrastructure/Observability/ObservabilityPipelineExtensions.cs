using ExamPlatform.SharedKernel.Infrastructure.Health;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ExamPlatform.SharedKernel.Infrastructure.Observability;

/// <summary>Adds the correlation and request-logging middleware, and the health endpoints (NFR-3, NFR-9).</summary>
public static class ObservabilityPipelineExtensions
{
    /// <summary>The original health route, kept as it was: it runs no checks, so Render's probe does not depend on the database.</summary>
    public const string HealthPath = "/v1/health";

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
    /// Maps the three health routes, each with its own set of checks. Render's health check and the web client use
    /// <see cref="HealthPath"/>, which keeps the check set it always had (none). Liveness has none too; readiness runs the
    /// database check.
    /// </summary>
    /// <remarks>
    /// Every route must have an explicit predicate. <c>MapHealthChecks</c> with no options runs every registered check, so the
    /// database check, which is registered for readiness only, would otherwise also run on <see cref="HealthPath"/>, and a database
    /// outage would take the whole service out of rotation with a 503.
    /// </remarks>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapExamPlatformHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks(HealthPath, new HealthCheckOptions { Predicate = SelectsNoChecks });
        endpoints.MapHealthChecks(LivenessPath, new HealthCheckOptions { Predicate = SelectsNoChecks });
        endpoints.MapHealthChecks(ReadinessPath, new HealthCheckOptions { Predicate = SelectsReadinessChecks });
        return endpoints;
    }

    /// <summary>The check selection for the routes that run no checks: no registration is selected.</summary>
    /// <param name="registration">A registered health check.</param>
    /// <returns>Always false.</returns>
    public static bool SelectsNoChecks(HealthCheckRegistration registration) => false;

    /// <summary>The check selection for readiness: only the checks tagged <see cref="DatabaseReadinessCheck.ReadyTag"/>.</summary>
    /// <param name="registration">A registered health check.</param>
    /// <returns>True when the registration carries the ready tag.</returns>
    public static bool SelectsReadinessChecks(HealthCheckRegistration registration) =>
        registration.Tags.Contains(DatabaseReadinessCheck.ReadyTag);
}
