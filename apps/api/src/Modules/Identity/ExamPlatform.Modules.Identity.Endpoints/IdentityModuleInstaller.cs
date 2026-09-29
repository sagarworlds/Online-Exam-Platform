using ExamPlatform.Modules.Identity.Application;
using ExamPlatform.Modules.Identity.Application.Commands;
using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Application.Queries;
using ExamPlatform.Modules.Identity.Endpoints.Authorization;
using ExamPlatform.Modules.Identity.Infrastructure;
using ExamPlatform.Modules.Identity.Infrastructure.Repositories;
using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ExamPlatform.Modules.Identity.Endpoints;

/// <summary>Registers and maps the Identity module (FR-1 through FR-4).</summary>
public sealed class IdentityModuleInstaller : IModuleInstaller
{
    /// <inheritdoc />
    public string ModuleName => "Identity";

    /// <inheritdoc />
    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<IdentityDbContext>((sp, options) => options
            .UseNpgsql(configuration.GetConnectionString("Postgres"))
            .AddInterceptors(sp.GetRequiredService<DomainEventsSaveChangesInterceptor>()));

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IOtpChallengeRepository, OtpChallengeRepository>();
        services.AddScoped<IPasswordResetTokenRepository, PasswordResetTokenRepository>();
        services.AddScoped<IIdentityUnitOfWork, IdentityUnitOfWork>();

        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<IOtpCodeGenerator, OtpCodeGenerator>();
        services.AddScoped<IOtpSender, LoggingOtpSender>();
        services.AddSingleton<ITokenGenerator, JwtTokenGenerator>();

        services.AddScoped<OtpChallengeIssuer>();
        services.AddScoped<LoginSessionIssuer>();
        services.AddScoped<RequestOtpHandler>();
        services.AddScoped<VerifyOtpHandler>();
        services.AddScoped<RegisterCandidateHandler>();
        services.AddScoped<PasswordLoginHandler>();
        services.AddScoped<RequestPasswordResetHandler>();
        services.AddScoped<ResetPasswordHandler>();
        services.AddScoped<AssignRoleHandler>();
        services.AddScoped<UpdateProfileHandler>();
        services.AddScoped<GetProfileHandler>();

        // Registered here because Identity owns what a "perm" claim means; it is the
        // only module in this slice that needs a custom IAuthorizationPolicyProvider,
        // so there is no risk of a second module's registration overwriting this one.
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapIdentityEndpoints();

    /// <inheritdoc />
    public async Task MigrateAndSeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<IdentityDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
        await IdentitySeeder.SeedAsync(db, cancellationToken);
    }
}
