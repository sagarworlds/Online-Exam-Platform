using ExamPlatform.Modules.ExamAuthoring.Application;
using ExamPlatform.Modules.ExamAuthoring.Application.Commands;
using ExamPlatform.Modules.ExamAuthoring.Application.Ports;
using ExamPlatform.Modules.ExamAuthoring.Application.Queries;
using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamAuthoring.Infrastructure;
using ExamPlatform.Modules.ExamAuthoring.Infrastructure.Repositories;
using ExamPlatform.Modules.QuestionBank.Contracts;
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
        services.AddScoped<IExamCatalog, ExamCatalog>();
        services.AddScoped<IQuestionUsageSource, ExamQuestionUsageSource>();
        services.AddScoped<IQuestionPlacementGuard, ExamScopePlacementGuard>();

        services.AddScoped<ExamScopeResolver>();
        services.AddScoped<ExamDtoFactory>();

        services.AddScoped<CreateExamHandler>();
        services.AddScoped<ListExamsHandler>();
        services.AddScoped<GetExamHandler>();
        services.AddScoped<ScheduleExamHandler>();
        services.AddScoped<AddSectionHandler>();
        services.AddScoped<AddExamQuestionHandler>();
        services.AddScoped<PublishExamHandler>();
        services.AddScoped<SetExamScopeHandler>();
        services.AddScoped<SetResultReleaseHandler>();
        services.AddScoped<ReleaseResultsHandler>();
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapExamAuthoringEndpoints();
    }

    /// <inheritdoc />
    public async Task MigrateAndSeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<ExamAuthoringDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }
}
