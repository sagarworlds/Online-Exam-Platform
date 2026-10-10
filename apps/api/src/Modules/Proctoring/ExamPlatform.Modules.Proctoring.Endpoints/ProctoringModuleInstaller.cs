using ExamPlatform.Modules.Proctoring.Application;
using ExamPlatform.Modules.Proctoring.Application.Commands;
using ExamPlatform.Modules.Proctoring.Application.Ports;
using ExamPlatform.Modules.Proctoring.Application.Queries;
using ExamPlatform.Modules.Proctoring.Contracts;
using ExamPlatform.Modules.Proctoring.Domain;
using ExamPlatform.Modules.Proctoring.Infrastructure;
using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ExamPlatform.Modules.Proctoring.Endpoints;

/// <summary>Registers and maps the Proctoring module: the risk score and the human review of flagged attempts (FR-27).</summary>
public sealed class ProctoringModuleInstaller : IModuleInstaller
{
    /// <inheritdoc />
    public string ModuleName => "Proctoring";

    /// <inheritdoc />
    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ProctoringDbContext>((sp, options) => options
            .UseNpgsql(configuration.GetConnectionString("Postgres"))
            .AddInterceptors(sp.GetRequiredService<DomainEventsSaveChangesInterceptor>()));

        // The weights and thresholds are bound as options and checked when the host starts. The policy is built from the bound value
        // rather than read here, for the same reason as the dispute window: a test's configuration is layered on after AddModule runs.
        services.AddOptions<RiskScoringOptions>()
            .Bind(configuration.GetSection(RiskScoringOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<RiskScoringOptions>, RiskScoringOptionsValidator>();
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<RiskScoringOptions>>().Value.ToPolicy());

        services.AddScoped<IRiskAssessmentRepository, RiskAssessmentRepository>();
        services.AddScoped<IProctoringUnitOfWork, ProctoringUnitOfWork>();
        services.AddScoped<ProctoringAuditTrail>();
        services.AddScoped<RunRiskScanHandler>();
        services.AddScoped<ListRiskFlagsHandler>();
        services.AddScoped<ReviewRiskFlagHandler>();
        services.AddScoped<DismissRiskFlagHandler>();
        services.AddScoped<IRiskFlagReader, RiskFlagReader>();
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapProctoringEndpoints();

    /// <inheritdoc />
    public async Task MigrateAndSeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<ProctoringDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }
}
