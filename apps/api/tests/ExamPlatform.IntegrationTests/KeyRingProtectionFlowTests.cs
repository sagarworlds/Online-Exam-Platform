using System.Security.Cryptography;
using ExamPlatform.Modules.QuestionBank.Endpoints;
using ExamPlatform.Modules.QuestionBank.Infrastructure;
using ExamPlatform.Modules.QuestionBank.Infrastructure.Encryption;
using ExamPlatform.SharedKernel.Infrastructure;
using ExamPlatform.SharedKernel.Infrastructure.KeyRing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// The protection of the Data Protection key ring against a real Postgres, through the QuestionBank module's own installer (NFR-5, #86). It
/// follows what a deployment goes through: a ring stored in the clear (every deployment before this change), the upgrade that protects it, a
/// restart, and the mistakes (a wrong key, no key). The tests that go through the whole Host replace the key ring with an in-memory one
/// (<see cref="SharedKeyRing"/>), so none of them reaches the database-backed ring that these tests are for.
/// </summary>
public sealed class KeyRingProtectionFlowTests(PostgresServerFixture postgres) : IClassFixture<PostgresServerFixture>
{
    private const string Content = "What is 2 + 2?";

    // EF Core caches the QuestionBank model for the whole process, and that model keeps the content cipher of the host that built it first.
    // These tests dispose their hosts, so one of them must never be the first (see SharedKeyRing). This builds the model once, with the
    // shared in-memory key ring that outlives every test, before any host of this class exists.
    private static readonly Lazy<bool> SharedModel = new(() =>
    {
        using var host = BuildHost("Host=localhost;Port=1;Database=unused;Username=unused;Password=unused", "Development", key: null, useSharedKeyRing: true);
        using var scope = host.CreateScope();
        _ = scope.ServiceProvider.GetRequiredService<QuestionBankDbContext>().Model;
        return true;
    });

    private static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(KeyRingProtectionOptions.KeySizeBytes));

    /// <summary>The module's services, registered by its own installer, as a host would have them; no web host is started.</summary>
    private static ServiceProvider BuildHost(
        string connectionString, string environment, string? key, string[]? previousKeys = null, bool useSharedKeyRing = false)
    {
        var settings = new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = connectionString };
        if (key is not null)
            settings[KeyRingProtectionOptions.KeySettingName] = key;

        var previous = previousKeys ?? [];
        for (var index = 0; index < previous.Length; index++)
            settings[$"{KeyRingProtectionOptions.PreviousKeysSettingName}:{index}"] = previous[index];

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(environment));
        services.AddSharedKernel();
        new QuestionBankModuleInstaller().AddModule(services, configuration);
        if (useSharedKeyRing)
            SharedKeyRing.Apply(services);

        return services.BuildServiceProvider();
    }

    /// <summary>A host for one step of a test. The shared model is built first, so this host is never the one whose cipher EF Core keeps.</summary>
    private static ServiceProvider Host(string connectionString, string environment, string? key, params string[] previousKeys)
    {
        _ = SharedModel.Value;
        return BuildHost(connectionString, environment, key, previousKeys);
    }

    /// <summary>What the job or a starting host does: migrate, protect the key ring, and run the content backfill.</summary>
    private static async Task MigrateAndSeedAsync(ServiceProvider host)
    {
        using var scope = host.CreateScope();
        await new QuestionBankModuleInstaller().MigrateAndSeedAsync(scope.ServiceProvider, CancellationToken.None);
    }

    private static string Encrypt(ServiceProvider host, string text) => host.GetRequiredService<QuestionContentCipher>().Encrypt(text);

    private static string Decrypt(ServiceProvider host, string stored) => host.GetRequiredService<QuestionContentCipher>().Decrypt(stored);

    /// <summary>The key ring rows as a copy of the database would hold them, read with plain SQL so nothing of the application is in between.</summary>
    private static async Task<IReadOnlyList<string>> ReadRingAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT \"Xml\" FROM \"questionBank\".\"DataProtectionKeys\" ORDER BY \"Id\"", connection);
        await using var reader = await command.ExecuteReaderAsync();

        var rows = new List<string>();
        while (await reader.ReadAsync())
            rows.Add(reader.GetString(0));

        return rows;
    }

    private static IEnumerable<Exception> Chain(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            yield return current;
    }

    [Fact]
    public async Task ARingStoredInTheClear_IsProtectedByMigrateAndSeed_AndTheContentItOpensStillOpens()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        var key = NewKey();

        // Every deployment before this change: the keys are in the table in the clear, and content is encrypted under them.
        string stored;
        using (var legacy = Host(connectionString, "Development", key: null))
        {
            await MigrateAndSeedAsync(legacy);
            stored = Encrypt(legacy, Content);
        }

        var before = await ReadRingAsync(connectionString);
        Assert.NotEmpty(before);
        Assert.All(before, xml => Assert.Contains("masterKey", xml, StringComparison.Ordinal));

        // The upgrade: the same database, now with the key. The migrate-and-seed step is what protects the ring.
        using (var upgraded = Host(connectionString, "Production", key))
        {
            await MigrateAndSeedAsync(upgraded);

            var after = await ReadRingAsync(connectionString);
            Assert.Equal(before.Count, after.Count);
            Assert.All(after, xml =>
            {
                Assert.StartsWith($"<{KeyRingEnvelope.ElementName}", xml, StringComparison.Ordinal);
                Assert.DoesNotContain("masterKey", xml, StringComparison.Ordinal);
            });

            // The key inside each entry is unchanged, so content encrypted before the upgrade is still readable.
            Assert.Equal(Content, Decrypt(upgraded, stored));
        }

        // A restart reads the protected ring back and makes no new key.
        var rowsAfterUpgrade = (await ReadRingAsync(connectionString)).Count;
        using (var restarted = Host(connectionString, "Production", key))
        {
            Assert.Equal(Content, Decrypt(restarted, stored));
            await MigrateAndSeedAsync(restarted);
        }

        Assert.Equal(rowsAfterUpgrade, (await ReadRingAsync(connectionString)).Count);
    }

    [Fact]
    public async Task AWrongKey_StopsTheHost_AndMakesNoNewKey()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        string stored;
        using (var first = Host(connectionString, "Production", NewKey()))
        {
            await MigrateAndSeedAsync(first);
            stored = Encrypt(first, Content);
        }

        var rowsBefore = await ReadRingAsync(connectionString);

        using var other = Host(connectionString, "Production", NewKey());
        var reading = Assert.ThrowsAny<Exception>(() => Decrypt(other, stored));
        var migrating = await Assert.ThrowsAnyAsync<Exception>(() => MigrateAndSeedAsync(other));

        Assert.Single(Chain(reading).OfType<KeyRingProtectionException>());
        Assert.Single(Chain(migrating).OfType<KeyRingProtectionException>());

        // Nothing was added or rewritten: a new key here would have made the first host's content unreadable.
        Assert.Equal(rowsBefore, await ReadRingAsync(connectionString));
    }

    [Fact]
    public async Task AProductionJobOnAFreshDatabase_NeedsNoKey()
    {
        // The migrate-and-seed job is meant to need only a connection string (ADR 0002). With no key-ring entries there is nothing to protect.
        var connectionString = await postgres.CreateDatabaseAsync();
        using var job = Host(connectionString, "Production", key: null);

        await MigrateAndSeedAsync(job);

        Assert.Empty(await ReadRingAsync(connectionString));
    }

    [Fact]
    public async Task AProductionJobOnADatabaseWithAKeyRing_ButNoKey_FailsNamingTheSetting()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        using (var legacy = Host(connectionString, "Development", key: null))
        {
            await MigrateAndSeedAsync(legacy);
            _ = Encrypt(legacy, Content);
        }

        var rowsBefore = await ReadRingAsync(connectionString);

        using var job = Host(connectionString, "Production", key: null);
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => MigrateAndSeedAsync(job));

        var invalid = Assert.Single(Chain(failure).OfType<OptionsValidationException>());
        Assert.Contains(KeyRingProtectionOptions.KeySettingName, invalid.Message);
        Assert.Equal(rowsBefore, await ReadRingAsync(connectionString));
    }

    [Fact]
    public async Task ANewProductionDatabase_ProtectsItsFirstKeyFromTheStart()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        var key = NewKey();
        string stored;
        using (var host = Host(connectionString, "Production", key))
        {
            await MigrateAndSeedAsync(host);
            stored = Encrypt(host, Content);
        }

        var rows = await ReadRingAsync(connectionString);
        Assert.NotEmpty(rows);
        Assert.All(rows, xml => Assert.DoesNotContain("masterKey", xml, StringComparison.Ordinal));

        using var restarted = Host(connectionString, "Production", key);
        Assert.Equal(Content, Decrypt(restarted, stored));
        Assert.Equal(rows.Count, (await ReadRingAsync(connectionString)).Count);
    }

    [Fact]
    public async Task Rotation_RewritesTheRingUnderTheNewKey_SoTheRetiredOneCanBeRemoved()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        var oldKey = NewKey();
        var newKey = NewKey();
        string stored;
        using (var first = Host(connectionString, "Production", oldKey))
        {
            await MigrateAndSeedAsync(first);
            stored = Encrypt(first, Content);
        }

        var underOldKey = await ReadRingAsync(connectionString);

        // The new key is current, the old one is listed as retired, and the start rewrites the ring under the new key.
        using (var rotating = Host(connectionString, "Production", newKey, oldKey))
        {
            await MigrateAndSeedAsync(rotating);
            Assert.Equal(Content, Decrypt(rotating, stored));
        }

        var underNewKey = await ReadRingAsync(connectionString);
        Assert.Equal(underOldKey.Count, underNewKey.Count);
        Assert.NotEqual(underOldKey, underNewKey);

        // The new key alone now opens the ring, and the old key alone no longer does.
        using (var newOnly = Host(connectionString, "Production", newKey))
            Assert.Equal(Content, Decrypt(newOnly, stored));

        using var oldOnly = Host(connectionString, "Production", oldKey);
        var failure = Assert.ThrowsAny<Exception>(() => Decrypt(oldOnly, stored));
        Assert.Single(Chain(failure).OfType<KeyRingProtectionException>());
    }

    private sealed class TestHostEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "key-ring-tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
