using ExamPlatform.Modules.Batch.Application;
using ExamPlatform.Modules.Batch.Application.Commands;
using ExamPlatform.Modules.Batch.Application.Ports;
using ExamPlatform.Modules.Batch.Infrastructure;
using ExamPlatform.Modules.Batch.Infrastructure.Repositories;
using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ExamPlatform.Modules.Batch.Endpoints;

/// <summary>Registers and maps the Batch module (M3: batch creation and member management).</summary>
public sealed class BatchModuleInstaller : IModuleInstaller
{
    /// <inheritdoc />
    public string ModuleName => "Batch";

    /// <inheritdoc />
    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<BatchDbContext>((sp, options) => options
            .UseNpgsql(configuration.GetConnectionString("Postgres"))
            .AddInterceptors(sp.GetRequiredService<DomainEventsSaveChangesInterceptor>()));

        services.AddScoped<IBatchRepository, EFBatchRepository>();
        services.AddScoped<IBatchUnitOfWork, BatchUnitOfWork>();
        services.AddScoped<IExamDeletionGuard, BatchExamDeletionGuard>();

        services.AddScoped<CreateBatchHandler>();
        services.AddScoped<AddBatchMemberHandler>();
        services.AddScoped<ActivateBatchHandler>();
        services.AddScoped<CloseBatchHandler>();
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapBatchEndpoints();
    }

    /// <inheritdoc />
    public async Task MigrateAndSeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<BatchDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }
}
