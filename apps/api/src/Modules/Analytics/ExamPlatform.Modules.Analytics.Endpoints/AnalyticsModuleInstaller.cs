using ExamPlatform.Modules.Analytics.Application;
using ExamPlatform.Modules.Analytics.Contracts;
using ExamPlatform.Modules.Analytics.Infrastructure;
using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ExamPlatform.Modules.Analytics.Endpoints;

/// <summary>Registers and maps the Analytics module: what candidates and staff learn from the results (FR-36, FR-37, FR-38).</summary>
public sealed class AnalyticsModuleInstaller : IModuleInstaller
{
    /// <inheritdoc />
    public string ModuleName => "Analytics";

    /// <inheritdoc />
    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AnalyticsDbContext>((sp, options) => options
            .UseNpgsql(configuration.GetConnectionString("Postgres"))
            .AddInterceptors(sp.GetRequiredService<DomainEventsSaveChangesInterceptor>()));

        services.AddScoped<IAnalyticsUnitOfWork, AnalyticsUnitOfWork>();
        services.AddScoped<ICandidateAnalytics, CandidateAnalyticsService>();
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapAnalyticsEndpoints();

    /// <inheritdoc />
    /// <remarks>
    /// The module has no tables yet, so it has no migration to apply and nothing to seed. Applying an empty model would only test the migrator
    /// against nothing; the export record that comes with FR-38 adds the first migration, and this method then runs it.
    /// </remarks>
    public Task MigrateAndSeedAsync(IServiceProvider services, CancellationToken cancellationToken) => Task.CompletedTask;
}
