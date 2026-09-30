using ExamPlatform.Modules.Invite.Application;
using ExamPlatform.Modules.Invite.Application.Commands;
using ExamPlatform.Modules.Invite.Application.Ports;
using ExamPlatform.Modules.Invite.Infrastructure;
using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ExamPlatform.Modules.Invite.Endpoints;

/// <summary>Registers and maps the Invite module (M3: exam invitations and code generation).</summary>
public sealed class InviteModuleInstaller : IModuleInstaller
{
    /// <inheritdoc />
    public string ModuleName => "Invite";

    /// <inheritdoc />
    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<InviteDbContext>((sp, options) => options
            .UseNpgsql(configuration.GetConnectionString("Postgres"))
            .AddInterceptors(sp.GetRequiredService<DomainEventsSaveChangesInterceptor>()));

        services.AddScoped<IInviteRepository, EFInviteRepository>();
        services.AddScoped<IInviteUnitOfWork, InviteUnitOfWork>();

        services.AddScoped<CreateInviteHandler>();
        services.AddScoped<GenerateInviteCodeHandler>();
        services.AddScoped<AcceptInviteHandler>();
        services.AddScoped<DeclineInviteHandler>();
        services.AddScoped<RevokeInviteHandler>();
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // TODO: Map Invite endpoints (/v1/invites/*)
    }

    /// <inheritdoc />
    public async Task MigrateAndSeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<InviteDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }
}
