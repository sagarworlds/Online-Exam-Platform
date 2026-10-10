using System.Xml.Linq;
using ExamPlatform.Modules.Identity.Application.Ports;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// Boots the real Host (<c>Program</c>) against a disposable Postgres container,
/// so integration tests exercise actual module wiring, EF Core migrations, and
/// HTTP routing rather than a hand-assembled subset of it. OTP delivery is
/// swapped for <see cref="CapturingOtpSender"/> so tests can read codes directly
/// instead of scraping log output. Not sealed: a suite that needs different
/// settings (e.g. a low rate limit to provoke a 429) subclasses it and overrides
/// <see cref="AdditionalConfiguration"/>.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
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

    /// <summary>
    /// Extra configuration layered over the factory's own settings (connection string,
    /// JWT keys); a key given here wins over the same key set by the factory. Subclasses
    /// that override this should start from <c>base.AdditionalConfiguration</c>, so any
    /// test-wide settings declared here keep applying.
    /// </summary>
    protected virtual IReadOnlyDictionary<string, string?> AdditionalConfiguration { get; } =
        new Dictionary<string, string?>
        {
            // Every TestServer request lands in the same client-IP partition, so the
            // production default (60/min) would make busier suites flake with 429s.
            ["RateLimiting:Global:PermitLimit"] = "100000",

            // Same reason for the Identity policies: a suite signs many users in and out
            // from the one client IP, far beyond what a real client would do. The 429
            // tests use LowAuthRateLimitApiFactory to bring individual ones back down.
            ["Identity:RateLimits:OtpRequest:PermitLimit"] = "100000",
            ["Identity:RateLimits:OtpVerify:PermitLimit"] = "100000",
            ["Identity:RateLimits:PasswordLogin:PermitLimit"] = "100000",
            ["Identity:RateLimits:PasswordReset:PermitLimit"] = "100000",

            // The host's own notification timer (FR-39) would send e-mails in the background of every suite; the tests that look at
            // notifications run a pass themselves, at a time they choose.
            ["Notifications:Enabled"] = "false",

            // The Development host reads the developer's user-secrets, which may name a
            // bootstrap administrator for their own database. Tests must not depend on that,
            // so it is switched off here; BootstrapAdminApiFactory turns it on deliberately.
            ["Identity:Bootstrap:AdminEmail"] = "",
            ["Identity:Bootstrap:AdminPassword"] = "",
        };

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development, not the test-process default: Program.cs only runs its
        // migrate-and-seed startup step (every module's IModuleInstaller.MigrateAndSeedAsync)
        // in Development, and the fresh container has no schema without it.
        builder.UseEnvironment("Development");

        // Errors the host logs (such as an unhandled exception behind a 500) are kept, so a failing request can report them.
        builder.ConfigureLogging(logging => logging.AddProvider(new ServerErrorLogProvider()));

        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = _postgres.GetConnectionString(),
            ["Jwt:SigningKey"] = "integration-test-only-signing-key-at-least-32-bytes",
            ["Jwt:Issuer"] = "exam-platform-tests",
            ["Jwt:Audience"] = "exam-platform-tests-clients",
            // The suites create the same question again and again; the duplicate tests turn this back on for their own host.
            ["QuestionBank:RefuseDuplicates"] = "false",
        };

        foreach (var (key, value) in AdditionalConfiguration)
        {
            settings[key] = value;
        }

        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IOtpSender>(OtpSender);
            TestRemoteIpStartupFilter.Register(services);
            SharedKeyRing.Apply(services);
        });
    }

    /// <summary>
    /// One Data Protection key ring for every test host in the process. EF Core caches the QuestionBank model for the process, and its
    /// value converters keep the content cipher of the host that built it first. A later host therefore decrypts with that first
    /// cipher, which works only if both hosts read the same keys. Production keeps its keys in the database
    /// (<c>PersistKeysToDbContext</c>); this replaces only where the test host keeps them.
    /// </summary>
    private static class SharedKeyRing
    {
        private static readonly MemoryKeyRepository Repository = new();

        static SharedKeyRing()
        {
            // The first key is created here, before any host runs: two hosts that each created one at the same moment would hold
            // different default keys.
            var services = new ServiceCollection();
            services.AddDataProtection().SetApplicationName("ExamPlatform");
            services.PostConfigure<KeyManagementOptions>(options => options.XmlRepository = Repository);
            using var provider = services.BuildServiceProvider();
            provider.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("ExamPlatform.IntegrationTests.KeyRingWarmUp")
                .Protect("warm-up");
        }

        /// <summary>Points a test host's Data Protection at the shared key ring. PostConfigure runs after the host's own key storage is set.</summary>
        public static void Apply(IServiceCollection services) =>
            services.PostConfigure<KeyManagementOptions>(options => options.XmlRepository = Repository);

        private sealed class MemoryKeyRepository : IXmlRepository
        {
            private readonly object _gate = new();
            private readonly List<XElement> _elements = [];

            public IReadOnlyCollection<XElement> GetAllElements()
            {
                lock (_gate)
                {
                    return _elements.Select(element => new XElement(element)).ToArray();
                }
            }

            public void StoreElement(XElement element, string friendlyName)
            {
                lock (_gate)
                {
                    _elements.Add(new XElement(element));
                }
            }
        }
    }
}
