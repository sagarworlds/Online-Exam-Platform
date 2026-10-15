using System.Security.Cryptography;
using System.Xml.Linq;
using ExamPlatform.SharedKernel.Infrastructure.KeyRing;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamPlatform.SharedKernel.UnitTests;

/// <summary>
/// The protection of the Data Protection key ring (NFR-5, #86), tested against the real Data Protection stack with an in-memory table in the
/// place of the database. What matters is what a copy of the table would hold, that a restart still opens what was protected, and that a
/// wrong or missing key stops the host instead of making a new key.
/// </summary>
public class KeyRingProtectionTests
{
    private static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(KeyRingProtectionOptions.KeySizeBytes));

    private static byte[] Bytes(string key) => Convert.FromBase64String(key);

    /// <summary>
    /// A host's Data Protection services, set up as the QuestionBank installer sets them up: a repository is chosen first (here the in-memory
    /// table, as <c>PersistKeysToDbContext</c> would choose the database), and then the ring is protected.
    /// </summary>
    private static ServiceProvider Host(MemoryKeyRepository repository, string environment, string? key, params string[] previousKeys)
    {
        var settings = new Dictionary<string, string?>();
        if (key is not null)
            settings[KeyRingProtectionOptions.KeySettingName] = key;

        for (var index = 0; index < previousKeys.Length; index++)
            settings[$"{KeyRingProtectionOptions.PreviousKeysSettingName}:{index}"] = previousKeys[index];

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddSingleton<IHostEnvironment>(new FakeHostEnvironment(environment));
        services.AddDataProtection().SetApplicationName("key-ring-tests");
        services.Configure<KeyManagementOptions>(options => options.XmlRepository = repository);
        services.AddDataProtection().ProtectKeyRing();
        return services.BuildServiceProvider();
    }

    private static IDataProtector Protector(ServiceProvider host) =>
        host.GetRequiredService<IDataProtectionProvider>().CreateProtector("key-ring-tests.purpose");

    private static IEnumerable<Exception> Chain(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            yield return current;
    }

    private static string Text(XElement element) => element.ToString(SaveOptions.DisableFormatting);

    private static XElement SampleEntry() => new("key", new XAttribute("id", Guid.NewGuid()), new XElement("masterKey", "c2VjcmV0"));

    [Fact]
    public void EveryStoredEntry_IsAnEnvelope_AndHoldsNoKeyMaterialInTheClear()
    {
        var repository = new MemoryKeyRepository();
        var key = NewKey();
        using var host = Host(repository, "Production", key);

        Protector(host).Protect("question text");

        Assert.NotEmpty(repository.Stored);
        Assert.All(repository.Stored, entry =>
        {
            Assert.True(KeyRingEnvelope.IsEnvelope(entry));
            Assert.DoesNotContain("masterKey", entry.ToString(), StringComparison.Ordinal);
        });

        // The same entries do hold key material once they are opened, so the check above is not passing for want of anything to hide.
        var opener = new AesGcmKeyRingProtector(Bytes(key), []);
        Assert.Contains(repository.Stored, entry => opener.Unprotect(entry).ToString().Contains("masterKey", StringComparison.Ordinal));
    }

    [Fact]
    public void ARestartedHost_ReadsItsKeysBack_AndOpensWhatWasProtectedBefore()
    {
        var repository = new MemoryKeyRepository();
        var key = NewKey();
        string payload;
        using (var first = Host(repository, "Production", key))
            payload = Protector(first).Protect("question text");
        var storesBefore = repository.StoreCount;

        using var restarted = Host(repository, "Production", key);

        Assert.Equal("question text", Protector(restarted).Unprotect(payload));
        Assert.Equal(storesBefore, repository.StoreCount);
    }

    [Fact]
    public void AWrongKey_StopsTheHost_AndNoNewKeyIsMade()
    {
        var repository = new MemoryKeyRepository();
        string payload;
        using (var first = Host(repository, "Production", NewKey()))
            payload = Protector(first).Protect("question text");
        var storesBefore = repository.StoreCount;

        using var other = Host(repository, "Production", NewKey());
        var failure = Assert.ThrowsAny<Exception>(() => Protector(other).Unprotect(payload));

        // Making a new key here would make the first host's content unreadable, with the cause hidden. It is refused and named instead.
        var cause = Assert.Single(Chain(failure).OfType<KeyRingProtectionException>());
        Assert.Contains(KeyRingProtectionOptions.KeySettingName, cause.Message);
        Assert.Contains(KeyRingProtectionOptions.PreviousKeysSettingName, cause.Message);
        Assert.Equal(storesBefore, repository.StoreCount);

        // And protecting new content is refused for the same reason, not answered with a new key.
        Assert.ThrowsAny<Exception>(() => Protector(other).Protect("more text"));
        Assert.Equal(storesBefore, repository.StoreCount);
    }

    [Fact]
    public void AHostWithNoKey_CannotOpenAProtectedRing_AndMakesNoNewKey()
    {
        var repository = new MemoryKeyRepository();
        string payload;
        using (var first = Host(repository, "Production", NewKey()))
            payload = Protector(first).Protect("question text");
        var storesBefore = repository.StoreCount;

        using var development = Host(repository, "Development", key: null);
        var failure = Assert.ThrowsAny<Exception>(() => Protector(development).Unprotect(payload));

        var cause = Assert.Single(Chain(failure).OfType<KeyRingProtectionException>());
        Assert.Contains(KeyRingProtectionOptions.KeySettingName, cause.Message);
        Assert.Equal(storesBefore, repository.StoreCount);
    }

    [Fact]
    public void ADevelopmentHostWithNoKey_StoresTheRingInTheClear_AsItAlwaysDid()
    {
        var repository = new MemoryKeyRepository();
        using var host = Host(repository, "Development", key: null);

        Protector(host).Protect("question text");

        Assert.All(repository.Stored, entry => Assert.False(KeyRingEnvelope.IsEnvelope(entry)));
        Assert.Contains(repository.Stored, entry => entry.ToString().Contains("masterKey", StringComparison.Ordinal));
    }

    [Fact]
    public void ARingStoredInTheClear_StillOpens_AndIsProtectedWithoutLosingAKey()
    {
        // The state of every existing deployment: keys stored in the clear, content already encrypted under them.
        var repository = new MemoryKeyRepository();
        string payload;
        using (var legacy = Host(repository, "Development", key: null))
            payload = Protector(legacy).Protect("old content");
        Assert.All(repository.Stored, entry => Assert.False(KeyRingEnvelope.IsEnvelope(entry)));

        var key = NewKey();
        var protector = new AesGcmKeyRingProtector(Bytes(key), []);
        using (var upgraded = Host(repository, "Production", key))
        {
            // Until it has been rewritten, the ring is read as it is stored, so the upgrade never stops the host.
            Assert.Equal("old content", Protector(upgraded).Unprotect(payload));

            // What the migrate-and-seed step does, row by row: rewrite each entry under the key.
            repository.ReplaceAll(repository.Stored.Select(entry =>
                XElement.Parse(protector.Reprotect(Text(entry)) ?? throw new InvalidOperationException("A clear entry must be rewritten."))));
        }

        Assert.All(repository.Stored, entry =>
        {
            Assert.True(KeyRingEnvelope.IsEnvelope(entry));
            Assert.DoesNotContain("masterKey", entry.ToString(), StringComparison.Ordinal);
        });
        var storesBefore = repository.StoreCount;

        // The key inside the entry is unchanged, so the content encrypted before is still readable, and no new key was needed.
        using var restarted = Host(repository, "Production", key);
        Assert.Equal("old content", Protector(restarted).Unprotect(payload));
        Assert.Equal(storesBefore, repository.StoreCount);
    }

    [Fact]
    public void Reprotect_LeavesAnEntryAlone_WhenItIsAlreadyUnderTheCurrentKey()
    {
        var protector = new AesGcmKeyRingProtector(RandomNumberGenerator.GetBytes(32), []);
        var stored = Text(protector.Protect(SampleEntry()));

        Assert.Null(protector.Reprotect(stored));
    }

    [Fact]
    public void Rotation_AnEntryUnderARetiredKey_OpensAndIsRewrittenUnderTheNewOne()
    {
        var oldKey = RandomNumberGenerator.GetBytes(32);
        var newKey = RandomNumberGenerator.GetBytes(32);
        var entry = SampleEntry();
        var storedUnderOld = Text(new AesGcmKeyRingProtector(oldKey, []).Protect(entry));

        var rotated = new AesGcmKeyRingProtector(newKey, [oldKey]);
        Assert.Equal(entry.ToString(), rotated.Unprotect(XElement.Parse(storedUnderOld)).ToString());

        var rewritten = rotated.Reprotect(storedUnderOld);
        Assert.NotNull(rewritten);

        // Rewritten, the entry needs only the new key, so the retired one can be removed from the configuration.
        var newOnly = new AesGcmKeyRingProtector(newKey, []);
        Assert.Equal(entry.ToString(), newOnly.Unprotect(XElement.Parse(rewritten)).ToString());
        Assert.Throws<KeyRingProtectionException>(() => new AesGcmKeyRingProtector(oldKey, []).Unprotect(XElement.Parse(rewritten)));

        // And an entry under a retired key that is not listed is refused, with the setting to put it in.
        var missing = Assert.Throws<KeyRingProtectionException>(() => newOnly.Unprotect(XElement.Parse(storedUnderOld)));
        Assert.Contains(KeyRingProtectionOptions.PreviousKeysSettingName, missing.Message);
    }

    [Fact]
    public void ATamperedEntry_IsRefused()
    {
        var protector = new AesGcmKeyRingProtector(RandomNumberGenerator.GetBytes(32), []);
        var envelope = protector.Protect(SampleEntry());
        var payload = Convert.FromBase64String(envelope.Value);
        payload[^1] ^= 0x01;
        envelope.Value = Convert.ToBase64String(payload);

        var failure = Assert.Throws<KeyRingProtectionException>(() => protector.Unprotect(envelope));

        Assert.IsAssignableFrom<CryptographicException>(failure.InnerException);
    }

    [Fact]
    public void AnEnvelopeOfAFormatThisBuildDoesNotKnow_IsRefused()
    {
        var protector = new AesGcmKeyRingProtector(RandomNumberGenerator.GetBytes(32), []);
        var envelope = protector.Protect(SampleEntry());
        envelope.SetAttributeValue(KeyRingEnvelope.VersionAttribute, "2");

        var failure = Assert.Throws<KeyRingProtectionException>(() => protector.Unprotect(envelope));

        Assert.Contains("version", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProtectingTheSameEntryTwice_GivesDifferentCiphertext_BecauseEachGetsItsOwnNonce()
    {
        var protector = new AesGcmKeyRingProtector(RandomNumberGenerator.GetBytes(32), []);
        var entry = SampleEntry();

        Assert.NotEqual(protector.Protect(entry).Value, protector.Protect(entry).Value);
    }

    [Fact]
    public void AKeyOfTheWrongSize_IsRefusedWhenAProtectorIsBuilt()
    {
        Assert.Throws<ArgumentException>(() => new AesGcmKeyRingProtector(new byte[16], []));
        Assert.Throws<ArgumentException>(() => new AesGcmKeyRingProtector(new byte[32], [new byte[31]]));
    }

    [Fact]
    public void AProductionHostWithNoKey_IsNotAskedForOneUntilTheRingIsUsed()
    {
        // The migrate-and-seed job on an empty database builds these services and never reads a key, so it needs only a connection string.
        var repository = new MemoryKeyRepository();
        using var host = Host(repository, "Production", key: null);

        var provider = host.GetRequiredService<IDataProtectionProvider>();
        var protector = provider.CreateProtector("key-ring-tests.purpose");

        var failure = Assert.ThrowsAny<Exception>(() => protector.Protect("question text"));
        var invalid = Assert.Single(Chain(failure).OfType<OptionsValidationException>());
        Assert.Contains(KeyRingProtectionOptions.KeySettingName, invalid.Message);
        Assert.Empty(repository.Stored);
    }

    [Fact]
    public void ProtectKeyRing_WithNoRepositoryChosen_IsRefused_InsteadOfLeavingTheKeysUnprotected()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IHostEnvironment>(new FakeHostEnvironment("Production"));
        services.AddDataProtection().SetApplicationName("key-ring-tests").ProtectKeyRing();
        using var host = services.BuildServiceProvider();

        var failure = Assert.ThrowsAny<Exception>(() => Protector(host).Protect("question text"));

        Assert.Contains(Chain(failure), exception => exception.Message.Contains("no repository", StringComparison.Ordinal));
    }

    [Fact]
    public void CallingProtectKeyRingTwice_WrapsTheRepositoryOnce()
    {
        var repository = new MemoryKeyRepository();
        var key = NewKey();
        var settings = new Dictionary<string, string?> { [KeyRingProtectionOptions.KeySettingName] = key };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddSingleton<IHostEnvironment>(new FakeHostEnvironment("Production"));
        services.AddDataProtection().SetApplicationName("key-ring-tests");
        services.Configure<KeyManagementOptions>(options => options.XmlRepository = repository);
        services.AddDataProtection().ProtectKeyRing().ProtectKeyRing();
        using var host = services.BuildServiceProvider();

        Protector(host).Protect("question text");

        // One layer: a single Unprotect gives the entry Data Protection wrote, not another envelope.
        var opened = new AesGcmKeyRingProtector(Bytes(key), []).Unprotect(repository.Stored[0]);
        Assert.Equal("key", opened.Name.LocalName);
    }

    [Fact]
    public void AnEntryThatCannotBeOpened_IsNeverSkipped_SoTheRingIsNeverReadAsSmallerThanItIs()
    {
        var protector = new AesGcmKeyRingProtector(RandomNumberGenerator.GetBytes(32), []);
        var inner = new MemoryKeyRepository();
        inner.StoreElement(protector.Protect(SampleEntry()), "key-1");
        var damaged = protector.Protect(SampleEntry());
        damaged.Value = "not base64 !!";
        inner.StoreElement(damaged, "key-2");
        var repository = new ProtectedXmlRepository(inner, () => protector, new CapturingLogger<ProtectedXmlRepository>());

        Assert.Throws<KeyRingProtectionException>(() => repository.GetAllElements());
    }

    [Fact]
    public void EntriesStoredInTheClear_AreReportedOnce_NotOnEveryRead()
    {
        var protector = new AesGcmKeyRingProtector(RandomNumberGenerator.GetBytes(32), []);
        var inner = new MemoryKeyRepository();
        inner.StoreElement(SampleEntry(), "key-1");
        var logger = new CapturingLogger<ProtectedXmlRepository>();
        var repository = new ProtectedXmlRepository(inner, () => protector, logger);

        var first = repository.GetAllElements();
        repository.GetAllElements();

        Assert.Single(first);
        var warning = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Contains(KeyRingProtectionOptions.KeySettingName, warning.Message);
    }

    [Fact]
    public void TheProtectorIsNotAskedForUntilTheRepositoryIsUsed()
    {
        var asked = 0;
        var repository = new ProtectedXmlRepository(
            new MemoryKeyRepository(),
            () =>
            {
                asked++;
                return new UnprotectedKeyRing();
            },
            new CapturingLogger<ProtectedXmlRepository>());

        Assert.Equal(0, asked);

        repository.GetAllElements();

        Assert.Equal(1, asked);
    }
}
