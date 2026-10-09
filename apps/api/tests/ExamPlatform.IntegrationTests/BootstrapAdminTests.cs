using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ExamPlatform.Modules.Identity.Application.Exceptions;
using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain;
using ExamPlatform.Modules.Identity.Domain.Rbac;
using ExamPlatform.Modules.Identity.Endpoints;
using ExamPlatform.Modules.Identity.Infrastructure;
using ExamPlatform.SharedKernel.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// An <see cref="ApiFactory"/> whose Development host is configured with a first administrator
/// (<c>Identity:Bootstrap</c>), as a developer would with user-secrets.
/// </summary>
public sealed class BootstrapAdminApiFactory : ApiFactory
{
    /// <summary>The login name of the configured administrator.</summary>
    public const string AdminEmail = "bootstrap.admin@tests.local";

    /// <summary>The configured administrator's password; a test value for the local test database only.</summary>
    public const string AdminPassword = "correct horse battery staple 42";

    /// <inheritdoc />
    protected override IReadOnlyDictionary<string, string?> AdditionalConfiguration =>
        new Dictionary<string, string?>(base.AdditionalConfiguration)
        {
            ["Identity:Bootstrap:AdminEmail"] = AdminEmail,
            ["Identity:Bootstrap:AdminPassword"] = AdminPassword,
        };
}

/// <summary>
/// The development-only first administrator (FR-2, FR-3): staff sign in with a password and a second
/// factor and nothing else creates one, so a fresh developer database needs this to reach any admin
/// feature. It must be idempotent, non-destructive, and never happen outside Development.
/// </summary>
public sealed class BootstrapAdminTests(BootstrapAdminApiFactory factory) : IClassFixture<BootstrapAdminApiFactory>
{
    [Fact]
    public async Task Startup_WithBootstrapConfigured_CreatesAnActiveSuperAdminWhoCanSignInThroughTwoFactor()
    {
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var admin = await db.Users.Include(u => u.Roles)
                .SingleAsync(u => u.Email == BootstrapAdminApiFactory.AdminEmail);

            Assert.Equal(UserStatus.Active, admin.Status);
            Assert.Equal([RbacCatalog.RoleNames.SuperAdmin], admin.Roles.Select(r => r.Name));

            // Only a hash is stored, and it verifies the configured password.
            Assert.NotNull(admin.PasswordHash);
            Assert.NotEqual(BootstrapAdminApiFactory.AdminPassword, admin.PasswordHash);
            Assert.True(scope.ServiceProvider.GetRequiredService<IPasswordHasher>()
                .Verify(BootstrapAdminApiFactory.AdminPassword, admin.PasswordHash));
        }

        // The real sign-in: password, then the second factor a SuperAdmin must complete (FR-3).
        using var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync(
            "/v1/auth/login",
            new { email = BootstrapAdminApiFactory.AdminEmail, password = BootstrapAdminApiFactory.AdminPassword });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var pending = await login.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(pending.GetProperty("requiresTwoFactor").GetBoolean());

        var verify = await client.PostAsJsonAsync("/v1/auth/otp/verify", new
        {
            otpChallengeId = pending.GetProperty("otpChallengeId").GetGuid(),
            code = factory.OtpSender.GetLastCode(BootstrapAdminApiFactory.AdminEmail),
        });
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        var accessToken = (await verify.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();

        // The signed-in administrator holds the permissions that open the admin features.
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/v1/admin/roles")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/v1/admin/audit-logs")).StatusCode);
    }

    [Fact]
    public async Task MigrateAndSeed_RunAgain_LeavesTheExistingAdministratorUntouched()
    {
        string originalHash;
        Guid originalId;
        using (var scope = factory.Services.CreateScope())
        {
            var admin = await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Users
                .SingleAsync(u => u.Email == BootstrapAdminApiFactory.AdminEmail);
            (originalId, originalHash) = (admin.Id, admin.PasswordHash!);

            // A second startup against the same database, as `dotnet run` twice would do.
            await new IdentityModuleInstaller().MigrateAndSeedAsync(scope.ServiceProvider, CancellationToken.None);
        }

        using var check = factory.Services.CreateScope();
        var admins = await check.ServiceProvider.GetRequiredService<IdentityDbContext>().Users
            .Where(u => u.Email == BootstrapAdminApiFactory.AdminEmail)
            .ToListAsync();
        var single = Assert.Single(admins);
        Assert.Equal(originalId, single.Id);
        Assert.Equal(originalHash, single.PasswordHash);
    }

    [Fact]
    public async Task MigrateAndSeed_OutsideDevelopment_IgnoresTheBootstrapSettings()
    {
        // The settings below ask for an administrator, but the environment says Production: no
        // account may come out of configuration on a deployed database.
        const string email = "production.admin@tests.local";
        using var scope = factory.Services.CreateScope();
        var production = new OverridingServiceProvider(scope.ServiceProvider)
            .With<IHostEnvironment>(new FixedEnvironment(Environments.Production))
            .With<IOptions<IdentityBootstrapOptions>>(Options.Create(new IdentityBootstrapOptions
            {
                AdminEmail = email,
                AdminPassword = BootstrapAdminApiFactory.AdminPassword,
            }));

        await new IdentityModuleInstaller().MigrateAndSeedAsync(production, CancellationToken.None);

        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.False(await db.Users.AnyAsync(u => u.Email == email));
    }

    [Theory]
    [InlineData("admin-only@tests.local", null)]
    [InlineData(null, "correct horse battery staple 42")]
    public async Task SeedAdminAsync_WithOnlyOneOfEmailAndPassword_FailsInsteadOfCreatingAnUnusableAccount(
        string? email, string? password)
    {
        using var scope = factory.Services.CreateScope();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => SeedAdminAsync(
            scope, new IdentityBootstrapOptions { AdminEmail = email, AdminPassword = password }));

        // Names the settings, never their values.
        Assert.Contains("Identity:Bootstrap:AdminPassword", failure.Message);
    }

    [Fact]
    public async Task SeedAdminAsync_WithAWeakPassword_IsRefusedAndCreatesNothing()
    {
        const string email = "weak.admin@tests.local";
        using var scope = factory.Services.CreateScope();

        await Assert.ThrowsAsync<WeakPasswordError>(() => SeedAdminAsync(
            scope, new IdentityBootstrapOptions { AdminEmail = email, AdminPassword = "short" }));

        Assert.False(await scope.ServiceProvider.GetRequiredService<IdentityDbContext>()
            .Users.AnyAsync(u => u.Email == email));
    }

    [Fact]
    public async Task SeedAdminAsync_WhenAnAccountWithThatEmailExists_DoesNotChangeIt()
    {
        var existing = await factory.SignInAsAsync("Candidate", email: $"existing-{Guid.NewGuid():N}@tests.local");
        using var scope = factory.Services.CreateScope();

        var outcome = await SeedAdminAsync(scope, new IdentityBootstrapOptions
        {
            AdminEmail = existing.Email,
            AdminPassword = BootstrapAdminApiFactory.AdminPassword,
        });

        Assert.Equal(BootstrapAdminOutcome.AlreadyExists, outcome);
        var user = await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Users
            .Include(u => u.Roles)
            .SingleAsync(u => u.Id == existing.UserId);
        Assert.Equal([RbacCatalog.RoleNames.Candidate], user.Roles.Select(r => r.Name));
        Assert.Null(user.PasswordHash);
    }

    [Fact]
    public async Task SeedAdminAsync_WithNothingConfigured_DoesNothing()
    {
        using var scope = factory.Services.CreateScope();

        Assert.Equal(BootstrapAdminOutcome.NotConfigured, await SeedAdminAsync(scope, new IdentityBootstrapOptions()));
    }

    private static Task<BootstrapAdminOutcome> SeedAdminAsync(IServiceScope scope, IdentityBootstrapOptions options)
    {
        var services = scope.ServiceProvider;
        return IdentityBootstrapSeeder.SeedAdminAsync(
            services.GetRequiredService<IdentityDbContext>(),
            options,
            services.GetRequiredService<IPasswordHasher>(),
            services.GetRequiredService<IPasswordPolicy>(),
            services.GetRequiredService<Clock>().UtcNow,
            CancellationToken.None);
    }

    // Lets a test run the installer's seed step as if the host were in another environment (or had
    // other options) while still using the real database and every other real service.
    private sealed class OverridingServiceProvider(IServiceProvider inner) : IServiceProvider
    {
        private readonly Dictionary<Type, object> _overrides = [];

        public OverridingServiceProvider With<T>(T instance) where T : class
        {
            _overrides[typeof(T)] = instance;
            return this;
        }

        public object? GetService(Type serviceType) =>
            _overrides.TryGetValue(serviceType, out var instance) ? instance : inner.GetService(serviceType);
    }

    private sealed class FixedEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "ExamPlatform.Api";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
