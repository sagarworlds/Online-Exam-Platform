using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ExamPlatform.SharedKernel.Application;

/// <summary>
/// A module's self-contained registration surface. The Host discovers and
/// invokes these; it never wires a module's internal services or endpoints
/// directly. Adding a new module to the platform means adding one instance to
/// the Host's module list — nothing else changes (Open/Closed Principle).
/// </summary>
public interface IModuleInstaller
{
    /// <summary>A short, human-readable name for logging and diagnostics.</summary>
    string ModuleName { get; }

    /// <summary>Registers the module's services (repositories, handlers, DbContext, etc.) into the container.</summary>
    /// <param name="services">The application's service collection.</param>
    /// <param name="configuration">The application's configuration.</param>
    void AddModule(IServiceCollection services, IConfiguration configuration);

    /// <summary>Maps the module's HTTP endpoints onto the application's routing surface.</summary>
    /// <param name="endpoints">The endpoint route builder to map routes onto.</param>
    void MapEndpoints(IEndpointRouteBuilder endpoints);

    /// <summary>
    /// Applies this module's pending EF Core migrations and any idempotent startup
    /// seeding. Called by the Host in Development only — routed through this
    /// interface, rather than the Host resolving the module's <c>DbContext</c>
    /// directly, so the Host never needs a reference to a module's Infrastructure
    /// project (module boundary rule, ADR 0001).
    /// </summary>
    /// <param name="services">The request-scoped service provider to resolve this module's services from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task MigrateAndSeedAsync(IServiceProvider services, CancellationToken cancellationToken);
}
