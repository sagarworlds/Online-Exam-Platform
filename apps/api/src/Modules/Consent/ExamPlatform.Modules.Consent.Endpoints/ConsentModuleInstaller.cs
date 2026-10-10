using ExamPlatform.Modules.Consent.Application;
using ExamPlatform.Modules.Consent.Application.Commands;
using ExamPlatform.Modules.Consent.Application.Ports;
using ExamPlatform.Modules.Consent.Application.Queries;
using ExamPlatform.Modules.Consent.Contracts;
using ExamPlatform.Modules.Consent.Infrastructure;
using ExamPlatform.Modules.Consent.Infrastructure.Repositories;
using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ExamPlatform.Modules.Consent.Endpoints;

/// <summary>Registers and maps the Consent module (FR-44), including the incident and breach log (FR-52).</summary>
public sealed class ConsentModuleInstaller : IModuleInstaller
{
    /// <inheritdoc />
    public string ModuleName => "Consent";

    /// <inheritdoc />
    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ConsentDbContext>((sp, options) => options
            .UseNpgsql(configuration.GetConnectionString("Postgres"))
            .AddInterceptors(sp.GetRequiredService<DomainEventsSaveChangesInterceptor>()));

        services.AddScoped<IConsentRecordRepository, ConsentRecordRepository>();
        services.AddScoped<INoticeVersionRepository, NoticeVersionRepository>();
        services.AddScoped<IConsentUnitOfWork, ConsentUnitOfWork>();
        services.AddScoped<IConsentService, ConsentService>();

        services.AddScoped<IIncidentRepository, IncidentRepository>();
        services.AddScoped<LogIncidentHandler>();
        services.AddScoped<ChangeIncidentStatusHandler>();
        services.AddScoped<ListOpenIncidentsHandler>();
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapConsentEndpoints();
        endpoints.MapIncidentEndpoints();
    }

    /// <inheritdoc />
    public async Task MigrateAndSeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<ConsentDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
        var clock = services.GetRequiredService<Clock>();
        await ConsentSeeder.SeedAsync(db, clock.UtcNow, cancellationToken);
    }
}
