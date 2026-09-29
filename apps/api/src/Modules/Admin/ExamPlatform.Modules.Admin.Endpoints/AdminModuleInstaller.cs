using ExamPlatform.Modules.Admin.Application;
using ExamPlatform.Modules.Admin.Application.Ports;
using ExamPlatform.Modules.Admin.Application.Queries;
using ExamPlatform.Modules.Admin.Contracts;
using ExamPlatform.Modules.Admin.Infrastructure;
using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ExamPlatform.Modules.Admin.Endpoints;

/// <summary>Registers and maps the Admin module (FR-40).</summary>
public sealed class AdminModuleInstaller : IModuleInstaller
{
    /// <inheritdoc />
    public string ModuleName => "Admin";

    /// <inheritdoc />
    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AdminDbContext>((sp, options) => options
            .UseNpgsql(configuration.GetConnectionString("Postgres"))
            .AddInterceptors(sp.GetRequiredService<DomainEventsSaveChangesInterceptor>()));

        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IAdminUnitOfWork, AdminUnitOfWork>();
        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<SearchAuditLogsHandler>();
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapAdminEndpoints();
}
