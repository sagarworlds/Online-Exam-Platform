using ExamPlatform.Modules.Notifications.Application;
using ExamPlatform.Modules.Notifications.Application.Commands;
using ExamPlatform.Modules.Notifications.Application.Ports;
using ExamPlatform.Modules.Notifications.Application.Queries;
using ExamPlatform.Modules.Notifications.Contracts;
using ExamPlatform.Modules.Notifications.Infrastructure;
using ExamPlatform.Modules.Notifications.Infrastructure.Repositories;
using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ExamPlatform.Modules.Notifications.Endpoints;

/// <summary>Registers and maps the Notifications module: the in-app feed (FR-39), which the other modules write to and each signed-in account reads.</summary>
public sealed class NotificationsModuleInstaller : IModuleInstaller
{
    /// <inheritdoc />
    public string ModuleName => "Notifications";

    /// <inheritdoc />
    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<NotificationsDbContext>((sp, options) => options
            .UseNpgsql(configuration.GetConnectionString("Postgres"))
            .AddInterceptors(sp.GetRequiredService<DomainEventsSaveChangesInterceptor>()));

        services.AddScoped<IInAppNotificationRepository, InAppNotificationRepository>();
        services.AddScoped<IInAppNotificationUnitOfWork, InAppNotificationUnitOfWork>();
        services.AddScoped<IInAppNotifier, InAppNotifier>();

        services.AddScoped<ListMyNotificationsHandler>();
        services.AddScoped<CountUnreadNotificationsHandler>();
        services.AddScoped<MarkNotificationReadHandler>();
        services.AddScoped<MarkAllNotificationsReadHandler>();
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapNotificationEndpoints();

    /// <inheritdoc />
    /// <remarks>The module has reference data of its own to seed: none. Its schema is applied the same way as the other modules'.</remarks>
    public async Task MigrateAndSeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<NotificationsDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }
}
