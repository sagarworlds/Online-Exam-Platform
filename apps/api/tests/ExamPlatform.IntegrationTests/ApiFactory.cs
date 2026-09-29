using ExamPlatform.Modules.Identity.Application.Ports;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// Boots the real Host (<c>Program</c>) against a disposable Postgres container,
/// so integration tests exercise actual module wiring, EF Core migrations, and
/// HTTP routing rather than a hand-assembled subset of it. OTP delivery is
/// swapped for <see cref="CapturingOtpSender"/> so tests can read codes directly
/// instead of scraping log output.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16")
        .WithDatabase("examplatform")
        .WithUsername("examplatform")
        .WithPassword("examplatform")
        .Build();

    /// <summary>The OTP sender test double, for reading codes issued during a test.</summary>
    public CapturingOtpSender OtpSender { get; } = new();

    /// <summary>Starts the Postgres container before any test in the class runs.</summary>
    public Task InitializeAsync() => _postgres.StartAsync();

    /// <inheritdoc />
    Task IAsyncLifetime.DisposeAsync() => _postgres.DisposeAsync().AsTask();

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development, not the test-process default: Program.cs only runs its
        // migrate-and-seed startup step (every module's IModuleInstaller.MigrateAndSeedAsync)
        // in Development, and the fresh container has no schema without it.
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = _postgres.GetConnectionString(),
            ["Jwt:SigningKey"] = "integration-test-only-signing-key-at-least-32-bytes",
            ["Jwt:Issuer"] = "exam-platform-tests",
            ["Jwt:Audience"] = "exam-platform-tests-clients",
        }));

        builder.ConfigureTestServices(services => services.AddSingleton<IOtpSender>(OtpSender));
    }
}
