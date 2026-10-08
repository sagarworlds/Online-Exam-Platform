using ExamPlatform.Modules.Identity.Application;
using ExamPlatform.Modules.Identity.Application.Commands;
using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Application.Queries;
using ExamPlatform.Modules.Identity.Application.Sessions;
using ExamPlatform.Modules.Identity.Contracts;
using ExamPlatform.Modules.Identity.Endpoints.Authentication;
using ExamPlatform.Modules.Identity.Endpoints.Authorization;
using ExamPlatform.Modules.Identity.Endpoints.OtpDelivery;
using ExamPlatform.Modules.Identity.Endpoints.RateLimiting;
using ExamPlatform.Modules.Identity.Infrastructure;
using ExamPlatform.Modules.Identity.Infrastructure.Repositories;
using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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

        // A second sign-in that ends an earlier session is audited from the event the user raises (FR-26).
        services.AddDomainEventHandlers(typeof(IdentityAuditTrail).Assembly);

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IOtpChallengeRepository, OtpChallengeRepository>();
        services.AddScoped<IPasswordResetTokenRepository, PasswordResetTokenRepository>();
        services.AddScoped<ISessionLookup, SessionLookup>();
        services.AddScoped<IStaffDirectory, StaffDirectory>();
        services.AddScoped<IContactDirectory, ContactDirectory>();
        services.AddScoped<IIdentityUnitOfWork, IdentityUnitOfWork>();

        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<IOtpCodeGenerator, OtpCodeGenerator>();
        services.AddSingleton<ITokenGenerator, JwtTokenGenerator>();
        AddOtpDelivery(services, configuration);
        AddRateLimitPolicies(services, configuration);

        services.AddSingleton<LoginEligibilityPolicy>();
        services.AddSingleton<IPasswordPolicy, PasswordPolicy>();
        services.AddScoped<OtpChallengeIssuer>();
        services.AddScoped<LoginSessionIssuer>();
        services.AddScoped<RequestOtpHandler>();
        services.AddScoped<VerifyOtpHandler>();
        services.AddScoped<RegisterCandidateHandler>();
        services.AddScoped<PasswordLoginHandler>();
        services.AddScoped<RequestPasswordResetHandler>();
        services.AddScoped<ResetPasswordHandler>();
        services.AddScoped<AssignRoleHandler>();
        services.AddScoped<LogoutHandler>();
        services.AddScoped<UpdateProfileHandler>();
        services.AddScoped<GetProfileHandler>();
        services.AddScoped<ListRolesHandler>();
        services.AddScoped<ListOutstandingOtpsHandler>();
        services.AddScoped<IWhatsAppDiagnostics, WhatsAppDiagnostics>();
        services.AddScoped<GetWhatsAppStatusHandler>();
        services.AddScoped<SendWhatsAppTestHandler>();
        services.AddScoped<GetWhatsAppDeliveryHandler>();

        // Development-only first administrator (see IdentityBootstrapOptions). Bound in every
        // environment so MigrateAndSeedAsync can tell that it was asked for and refuse it
        // outside Development; the options are read when the seed step runs, not captured here.
        services.AddOptions<IdentityBootstrapOptions>()
            .Bind(configuration.GetSection(IdentityBootstrapOptions.SectionName));

        // Registered here, not in the Host, because Identity owns what a token's "sid" claim
        // means (FR-4); the Host keeps referencing only Identity.Endpoints (ADR 0001). The
        // bearer handler resolves EventsType from the request's services on every request,
        // so the events, and the scoped DbContext behind SessionValidator, are per request.
        services.AddScoped<SessionValidator>();
        services.AddScoped<SessionValidatingJwtBearerEvents>();
        services.PostConfigure<JwtBearerOptions>(
            JwtBearerDefaults.AuthenticationScheme,
            options => options.EventsType = typeof(SessionValidatingJwtBearerEvents));

        // Registered here because Identity owns what a "perm" claim means; it is the
        // only module in this slice that needs a custom IAuthorizationPolicyProvider,
        // so there is no risk of a second module's registration overwriting this one.
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
    }

    // Picks the IOtpSender adapter named by Identity:OtpDelivery:Provider (NFR-6). The
    // provider is read when a sender is resolved, not here: a test's WebApplicationFactory
    // layers its configuration on after AddModule runs, so a value captured now could be
    // stale (the same concern as the JWT signing key in Program.cs).
    private static void AddOtpDelivery(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<OtpDeliveryOptions>()
            .Bind(configuration.GetSection(OtpDeliveryOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<OtpDeliveryOptions>, OtpDeliveryOptionsValidator>();

        // The development log that prints the codes also says why a sign-in got none; any other adapter says nothing.
        services.AddScoped<ISignInDiagnostics>(sp =>
            sp.GetRequiredService<IOptions<OtpDeliveryOptions>>().Value.Provider == OtpDeliveryOptions.DevelopmentLog
                ? ActivatorUtilities.CreateInstance<LoggingSignInDiagnostics>(sp)
                : new NoSignInDiagnostics());

        services.AddScoped<IOtpSender>(sp =>
        {
            var delivery = sp.GetRequiredService<IOptions<OtpDeliveryOptions>>().Value;
            IOtpSender sender = delivery.Provider switch
            {
                OtpDeliveryOptions.DevelopmentLog => ActivatorUtilities.CreateInstance<LoggingOtpSender>(sp),
                OtpDeliveryOptions.Smtp => ActivatorUtilities.CreateInstance<SmtpOtpSender>(sp),

                // Unreachable once the host has started: OtpDeliveryOptionsValidator refuses
                // any other provider at startup.
                var provider => throw new InvalidOperationException(
                    $"No IOtpSender adapter exists for {OtpDeliveryOptions.SectionName}:Provider '{provider}'."),
            };

            // Codes for phone numbers go over WhatsApp when the host says so; the adapter above keeps every other
            // channel. Without it the one adapter takes both channels, as it did before WhatsApp existed.
            return delivery.PhoneProvider == OtpDeliveryOptions.WhatsApp
                ? new ChannelRoutingOtpSender(sender, ActivatorUtilities.CreateInstance<WhatsAppOtpSender>(sp))
                : sender;
        });
    }

    // Adds Identity's named rate-limit policies (NFR-5) onto the Host's rate limiter, which
    // owns only the global limit. The limits are read when RateLimiterOptions is first
    // materialized, not captured here, for the same reason as the OTP provider above: a
    // test's WebApplicationFactory layers configuration on after AddModule runs.
    private static void AddRateLimitPolicies(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<IdentityRateLimitOptions>()
            .Bind(configuration.GetSection(IdentityRateLimitOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<IdentityRateLimitOptions>, IdentityRateLimitOptionsValidator>();

        services.AddOptions<RateLimiterOptions>()
            .Configure<IOptions<IdentityRateLimitOptions>>((rateLimiter, identityLimits) =>
            {
                var limits = identityLimits.Value;
                AddPolicy(rateLimiter, IdentityRateLimitPolicies.OtpRequest, limits.OtpRequest);
                AddPolicy(rateLimiter, IdentityRateLimitPolicies.OtpVerify, limits.OtpVerify);
                AddPolicy(rateLimiter, IdentityRateLimitPolicies.PasswordLogin, limits.PasswordLogin);
                AddPolicy(rateLimiter, IdentityRateLimitPolicies.PasswordReset, limits.PasswordReset);
            });
    }

    private static void AddPolicy(
        RateLimiterOptions rateLimiter, string policyName, IdentityRateLimitOptions.FixedWindowSettings settings) =>
        rateLimiter.AddPolicy(policyName, httpContext => ClientRateLimitPartition.FixedWindow(
            httpContext, settings.PermitLimit, TimeSpan.FromSeconds(settings.WindowSeconds)));

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapIdentityEndpoints();

    /// <inheritdoc />
    public async Task MigrateAndSeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<IdentityDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
        await IdentitySeeder.SeedAsync(db, cancellationToken);
        await SeedBootstrapAdminAsync(services, db, cancellationToken);
    }

    // Creates the configured first administrator, in Development only. In any other environment
    // the settings are ignored (with a warning, so a deployment that sets them is told why no
    // administrator appeared): an account created from configuration is a convenience for a
    // developer's empty database, never a way to provision staff on a real one.
    private static async Task SeedBootstrapAdminAsync(
        IServiceProvider services, IdentityDbContext db, CancellationToken cancellationToken)
    {
        var options = services.GetRequiredService<IOptions<IdentityBootstrapOptions>>().Value;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger<IdentityModuleInstaller>();

        if (!services.GetRequiredService<IHostEnvironment>().IsDevelopment())
        {
            if (options.IsRequested)
            {
                logger.LogWarning(
                    "{Section} is set but is only honoured in the Development environment; no administrator was created.",
                    IdentityBootstrapOptions.SectionName);
            }

            return;
        }

        var outcome = await IdentityBootstrapSeeder.SeedAdminAsync(
            db,
            options,
            services.GetRequiredService<IPasswordHasher>(),
            services.GetRequiredService<IPasswordPolicy>(),
            services.GetRequiredService<Clock>().UtcNow,
            cancellationToken);

        // The email is personal data (NFR-6), so only the outcome is logged.
        switch (outcome)
        {
            case BootstrapAdminOutcome.Created:
                logger.LogInformation("Created the development administrator configured in {Section}.", IdentityBootstrapOptions.SectionName);
                break;
            case BootstrapAdminOutcome.AlreadyExists:
                logger.LogInformation("The development administrator configured in {Section} already exists; left unchanged.", IdentityBootstrapOptions.SectionName);
                break;
        }
    }
}
