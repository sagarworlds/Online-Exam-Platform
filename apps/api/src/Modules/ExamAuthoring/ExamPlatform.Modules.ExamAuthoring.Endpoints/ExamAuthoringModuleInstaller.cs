using ExamPlatform.Modules.ExamAuthoring.Application;
using ExamPlatform.Modules.ExamAuthoring.Application.Commands;
using ExamPlatform.Modules.ExamAuthoring.Application.Ports;
using ExamPlatform.Modules.ExamAuthoring.Infrastructure;
using ExamPlatform.Modules.ExamAuthoring.Infrastructure.Repositories;
using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ExamPlatform.Modules.ExamAuthoring.Endpoints;

/// <summary>Registers and maps the ExamAuthoring module (M3: exam creation and management).</summary>
public sealed class ExamAuthoringModuleInstaller : IModuleInstaller
{
    /// <inheritdoc />
    public string ModuleName => "ExamAuthoring";

    /// <inheritdoc />
    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ExamAuthoringDbContext>((sp, options) => options
            .UseNpgsql(configuration.GetConnectionString("Postgres"))
            .AddInterceptors(sp.GetRequiredService<DomainEventsSaveChangesInterceptor>()));

        services.AddScoped<IExamRepository, EFExamRepository>();
        services.AddScoped<IExamAuthoringUnitOfWork, ExamAuthoringUnitOfWork>();

        services.AddScoped<CreateExamHandler>();
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // TODO: Map ExamAuthoring endpoints (/v1/exams/*)
    }

    /// <inheritdoc />
    public async Task MigrateAndSeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<ExamAuthoringDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }
}
