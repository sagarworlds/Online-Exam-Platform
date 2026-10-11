using ExamPlatform.Modules.Analytics.Application;
using ExamPlatform.Modules.Analytics.Application.Ports;
using ExamPlatform.Modules.Analytics.Contracts;
using ExamPlatform.Modules.Analytics.Infrastructure;
using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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

        // The item analysis threshold (FR-37) is bound as options and checked when the host starts, so a value below the floor stops the
        // application starting instead of showing indices that mean nothing. It is bound rather than read here because a test host layers its
        // configuration on after this runs.
        services.AddOptions<ItemAnalysisOptions>()
            .Bind(configuration.GetSection(ItemAnalysisOptions.SectionName))
            .Validate(
                options => options.MinimumCohortSize >= ItemAnalysisOptions.MinimumAllowedCohortSize,
                $"{ItemAnalysisOptions.SectionName}:MinimumCohortSize must be at least {ItemAnalysisOptions.MinimumAllowedCohortSize}.")
            .ValidateOnStart();
        services.AddSingleton(sp => new ItemAnalysisPolicy(sp.GetRequiredService<IOptions<ItemAnalysisOptions>>().Value.MinimumCohortSize));
        services.AddScoped<IExamItemAnalysis, ExamItemAnalysisService>();

        services.AddScoped<IItemAnalysisCsvWriter, ItemAnalysisCsvWriter>();
        services.AddScoped<IItemAnalysisExport, ItemAnalysisExportService>();
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
