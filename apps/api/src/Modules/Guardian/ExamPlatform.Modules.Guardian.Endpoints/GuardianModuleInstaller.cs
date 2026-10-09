using ExamPlatform.Modules.Guardian.Application;
using ExamPlatform.Modules.Guardian.Application.Commands;
using ExamPlatform.Modules.Guardian.Application.Ports;
using ExamPlatform.Modules.Guardian.Application.Queries;
using ExamPlatform.Modules.Guardian.Infrastructure;
using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ExamPlatform.Modules.Guardian.Endpoints;

/// <summary>Registers and maps the Guardian module (M3: guardian registration and consent delegation).</summary>
public sealed class GuardianModuleInstaller : IModuleInstaller
{
    /// <inheritdoc />
    public string ModuleName => "Guardian";

    /// <inheritdoc />
    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<GuardianDbContext>((sp, options) => options
            .UseNpgsql(configuration.GetConnectionString("Postgres"))
            .AddInterceptors(sp.GetRequiredService<DomainEventsSaveChangesInterceptor>()));

        services.AddScoped<IGuardianRepository, EFGuardianRepository>();
        services.AddScoped<IGuardianUnitOfWork, GuardianUnitOfWork>();

        services.AddScoped<CreateGuardianHandler>();
        services.AddScoped<LinkCandidateHandler>();
        services.AddScoped<RevokeGuardianLinkHandler>();
        services.AddScoped<UnlinkCandidateHandler>();
        services.AddScoped<ListGuardianLinksHandler>();
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGuardianEndpoints();
    }

    /// <inheritdoc />
    public async Task MigrateAndSeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<GuardianDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }
}
