using System.Diagnostics;
using ExamPlatform.Modules.Consent.Infrastructure;
using ExamPlatform.Modules.Identity.Domain.Rbac;
using ExamPlatform.Modules.Identity.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Testcontainers.PostgreSql;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// One disposable Postgres server for a test class, handing each test its own empty database, so
/// tests that migrate and seed never see each other's rows and the class pays for one container.
/// </summary>
public sealed class PostgresServerFixture : IAsyncLifetime
{
    private const string TemplateDatabase = "examplatform";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16")
        .WithDatabase(TemplateDatabase)
        .WithUsername("examplatform")
        .WithPassword("examplatform")
        .Build();

    /// <summary>Starts the container.</summary>
    public Task InitializeAsync() => _postgres.StartAsync();

    /// <inheritdoc />
    Task IAsyncLifetime.DisposeAsync() => _postgres.DisposeAsync().AsTask();

    /// <summary>Creates a new, empty database on the server.</summary>
    /// <returns>A connection string for the new database.</returns>
    // Why the suppression: CREATE DATABASE cannot take its name as a parameter. The name is the fixed prefix "test_" and a GUID in
    // its "N" form, so only lower-case hex characters can reach the statement.
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "The database name is a fixed prefix and a GUID in N format; no external input reaches the statement.")]
    public async Task<string> CreateDatabaseAsync()
    {
        var name = $"test_{Guid.NewGuid():N}";
        await using (var connection = new NpgsqlConnection(_postgres.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
            await command.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = name }.ConnectionString;
    }
}

/// <summary>
/// Outside Development nothing migrates or seeds the database on its own, so a deployment has to
/// (ADR 0002): <c>--migrate-and-seed</c> as a one-off job, or <c>Database:MigrateAndSeedOnStartup</c>.
/// These tests prove both leave a database where registration and the permission checks work
/// (roles, permissions and notice versions exist), that the job needs nothing but a connection
/// string, and that nothing in the development convenience (the bootstrap administrator) reaches
/// a production database.
/// </summary>
public sealed class NonDevelopmentStartupTests(PostgresServerFixture postgres) : IClassFixture<PostgresServerFixture>
{
    // Every module the Host composes, as named by its installer; each must report being migrated.
    private static readonly string[] ModuleNames =
        ["Identity", "Consent", "Admin", "ExamAuthoring", "Batch", "Invite", "Guardian"];

    private const string AdminEmail = "must.not.exist@tests.local";
    private const string AdminPassword = "correct horse battery staple 42";

    [Fact]
    public async Task ProductionEnvironment_WithMigrateAndSeedFlag_SeedsRolesPermissionsAndNotices()
    {
        var connectionString = await postgres.CreateDatabaseAsync();

        // A pre-deploy job has the database address and nothing else: no signing key and no OTP
        // provider. It must not be refused for lacking settings only a serving host needs.
        var run = await RunMigrateAndSeedJobAsync(connectionString);

        Assert.True(run.ExitCode == 0, run.Output);
        Assert.DoesNotContain("OtpDelivery", run.Output);
        foreach (var module in ModuleNames)
        {
            Assert.Contains($"Migrated and seeded {module}", run.Output);
        }

        var seeded = await ReadSeededDataAsync(connectionString);
        Assert.Equal(RbacCatalog.Permissions.Count, seeded.PermissionCount);
        Assert.Equal(RbacCatalog.Roles.Count, seeded.RoleCount);
        Assert.Equal(3, seeded.NoticeVersionCount);
        Assert.Equal(RbacCatalog.Roles.Sum(r => r.PermissionCodes.Count), seeded.GrantCount);
    }

    [Fact]
    public async Task MigrateAndSeedFlag_RunAgainstASeededDatabase_ChangesNothing()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        var first = await RunMigrateAndSeedJobAsync(connectionString);
        Assert.True(first.ExitCode == 0, first.Output);
        var afterFirst = await ReadSeededDataAsync(connectionString);

        // A redeploy runs the job again; it must neither fail nor duplicate or replace anything.
        var second = await RunMigrateAndSeedJobAsync(connectionString);

        Assert.True(second.ExitCode == 0, second.Output);
        Assert.Equal(afterFirst, await ReadSeededDataAsync(connectionString));
    }

    [Fact]
    public async Task MigrateAndSeedFlag_InProduction_IgnoresTheDevelopmentBootstrapAdministrator()
    {
        var connectionString = await postgres.CreateDatabaseAsync();

        var run = await RunMigrateAndSeedJobAsync(
            connectionString,
            new Dictionary<string, string>
            {
                ["Identity__Bootstrap__AdminEmail"] = AdminEmail,
                ["Identity__Bootstrap__AdminPassword"] = AdminPassword,
            });

        Assert.True(run.ExitCode == 0, run.Output);

        // Said once, without the address (personal data), so the operator knows why.
        Assert.Contains("only honoured in the Development environment", run.Output);
        Assert.DoesNotContain(AdminEmail, run.Output);

        await using var identity = IdentityContext(connectionString);
        Assert.False(await identity.Users.AnyAsync(u => u.Email == AdminEmail));
        Assert.Equal(0, await identity.Users.CountAsync());
    }

    [Fact]
    public async Task ProductionEnvironment_WithMigrateAndSeedOnStartupSetting_SeedsTheSameData()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        using var factory = new ProductionHostFactory(
            otpProvider: null,
            allowCapturingSender: true,
            extraSettings: new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = connectionString,
                ["Database:MigrateAndSeedOnStartup"] = "true",
            });

        using var client = factory.CreateClient();
        var response = await client.GetAsync("/v1/health");

        Assert.True(response.IsSuccessStatusCode);
        var seeded = await ReadSeededDataAsync(connectionString);
        Assert.Equal(RbacCatalog.Permissions.Count, seeded.PermissionCount);
        Assert.Equal(RbacCatalog.Roles.Count, seeded.RoleCount);
        Assert.Equal(3, seeded.NoticeVersionCount);
    }

    [Fact]
    public async Task DevelopmentEnvironment_WithMigrateAndSeedOnStartupDisabled_LeavesTheDatabaseUntouched()
    {
        // The setting, not the environment name, decides: a developer who turns it off gets
        // exactly what a deployment gets, an empty database until the job runs.
        var connectionString = await postgres.CreateDatabaseAsync();
        using var factory = new DevelopmentHostFactory(connectionString, migrateAndSeedOnStartup: false);

        using var client = factory.CreateClient();
        var response = await client.GetAsync("/v1/health");

        Assert.True(response.IsSuccessStatusCode);
        await using var identity = IdentityContext(connectionString);
        Assert.False(await identity.Database.CanConnectAsync() && (await identity.Database.GetAppliedMigrationsAsync()).Any());
    }

    private static IdentityDbContext IdentityContext(string connectionString) =>
        new(new DbContextOptionsBuilder<IdentityDbContext>().UseNpgsql(connectionString).Options);

    private static ConsentDbContext ConsentContext(string connectionString) =>
        new(new DbContextOptionsBuilder<ConsentDbContext>().UseNpgsql(connectionString).Options);

    private static async Task<SeededData> ReadSeededDataAsync(string connectionString)
    {
        await using var identity = IdentityContext(connectionString);
        await using var consent = ConsentContext(connectionString);

        var permissions = await identity.Permissions.OrderBy(p => p.Code).Select(p => p.Id).ToListAsync();
        var roles = await identity.Roles.OrderBy(r => r.Name).Select(r => r.Id).ToListAsync();
        var grants = await identity.Roles.SelectMany(r => r.Permissions).CountAsync();

        return new SeededData(
            permissions.Count,
            roles.Count,
            grants,
            await consent.NoticeVersions.CountAsync(),
            string.Join(',', permissions),
            string.Join(',', roles));
    }

    // Runs the real Host binary exactly as a deployment job would, as a separate process, so the
    // test covers the command-line flag, the early return before the web host starts, and the
    // exit code. Only the connection string is set; the rest is the Production default.
    private static async Task<JobRun> RunMigrateAndSeedJobAsync(
        string connectionString, IReadOnlyDictionary<string, string>? extraEnvironment = null)
    {
        var startInfo = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            WorkingDirectory = AppContext.BaseDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "ExamPlatform.Api.dll"));
        startInfo.ArgumentList.Add("--migrate-and-seed");
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        startInfo.Environment["DOTNET_ENVIRONMENT"] = "Production";
        startInfo.Environment["ConnectionStrings__Postgres"] = connectionString;
        foreach (var (key, value) in extraEnvironment ?? new Dictionary<string, string>())
        {
            startInfo.Environment[key] = value;
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The Host process could not be started.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("The Host did not exit after migrating and seeding; it must return before app.Run().");
        }

        return new JobRun(process.ExitCode, await stdout + await stderr);
    }

    private sealed record JobRun(int ExitCode, string Output);

    private sealed record SeededData(
        int PermissionCount, int RoleCount, int GrantCount, int NoticeVersionCount, string PermissionIds, string RoleIds);

    // The Development host used by every other suite, but with the startup migration setting forced.
    private sealed class DevelopmentHostFactory(string connectionString, bool migrateAndSeedOnStartup)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = connectionString,
                ["Jwt:SigningKey"] = "integration-test-only-signing-key-at-least-32-bytes",
                ["Jwt:Issuer"] = "exam-platform-tests",
                ["Jwt:Audience"] = "exam-platform-tests-clients",
                ["Database:MigrateAndSeedOnStartup"] = migrateAndSeedOnStartup ? "true" : "false",
                ["Identity:Bootstrap:AdminEmail"] = "",
                ["Identity:Bootstrap:AdminPassword"] = "",
            }));
        }
    }
}
