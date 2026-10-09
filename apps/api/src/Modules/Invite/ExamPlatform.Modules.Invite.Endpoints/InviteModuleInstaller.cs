using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.Invite.Application;
using ExamPlatform.Modules.Invite.Application.Commands;
using ExamPlatform.Modules.Invite.Application.Ports;
using ExamPlatform.Modules.Invite.Application.Queries;
using ExamPlatform.Modules.Invite.Contracts;
using ExamPlatform.Modules.Invite.Infrastructure;
using ExamPlatform.Modules.Invite.Infrastructure.Email;
using ExamPlatform.Modules.Invite.Infrastructure.WhatsApp;
using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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
        services.AddScoped<IEnrollments, EnrollmentReader>();
        services.AddScoped<IExamRoster, ExamRosterReader>();
        services.AddScoped<IExamDeletionGuard, InviteExamDeletionGuard>();

        // Invitations go out through the platform's mail sender when a mail server is configured (the "Smtp" section) and are
        // otherwise not sent at all: the inviter is given the link. There is deliberately no log-only sender, since a
        // link written to a log is a credential in a log.
        services.AddScoped<IInviteNotifier, SmtpInviteNotifier>();

        // The exam code also goes to the invited person's registered phone on WhatsApp, but only once the operator names an approved
        // template (Invite:WhatsApp:TemplateName). Naming one without the WhatsApp section filled in stops the host at start, instead of
        // every invitation quietly skipping WhatsApp.
        services.AddOptions<InviteWhatsAppOptions>()
            .Bind(configuration.GetSection(InviteWhatsAppOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<InviteWhatsAppOptions>, InviteWhatsAppOptionsValidator>();
        services.AddScoped<IInviteWhatsAppNotifier, WhatsAppInviteNotifier>();
        services.AddSingleton<IInviteLinkBuilder, ConfigurationInviteLinkBuilder>();

        services.AddScoped<CreateInviteHandler>();
        services.AddScoped<ListInvitesHandler>();
        services.AddScoped<GenerateInviteCodeHandler>();
        services.AddScoped<AcceptInviteHandler>();
        services.AddScoped<DeclineInviteHandler>();
        services.AddScoped<RevokeInviteHandler>();
        services.AddDomainEventHandlers(typeof(InviteAuditTrail).Assembly);
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapInviteEndpoints();
    }

    /// <inheritdoc />
    public async Task MigrateAndSeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<InviteDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }
}
