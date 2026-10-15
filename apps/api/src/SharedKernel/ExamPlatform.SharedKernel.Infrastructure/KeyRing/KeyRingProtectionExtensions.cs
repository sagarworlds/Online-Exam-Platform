using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamPlatform.SharedKernel.Infrastructure.KeyRing;

/// <summary>Registers the protection of the Data Protection key ring (NFR-5, #86).</summary>
public static class KeyRingProtectionExtensions
{
    /// <summary>
    /// Wraps the key ring's repository, whichever one was chosen, so its entries are encrypted with the key in
    /// <see cref="KeyRingProtectionOptions"/>. Call it after the repository is chosen, for example after <c>PersistKeysToDbContext</c>.
    /// </summary>
    /// <remarks>
    /// The wrapping is done as a post-configuration of <see cref="KeyManagementOptions"/>, which runs after every configuration, so it applies to
    /// the repository whatever the order of the calls. The settings are read when first needed, not when this is called, so a host that adds its
    /// configuration after registration is honoured. <c>ValidateOnStart</c> makes a serving host refuse to start without the key outside
    /// Development; a job that never uses the ring is not asked for it (see <see cref="ProtectedXmlRepository"/>).
    /// </remarks>
    /// <param name="builder">The Data Protection builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Raised when the key ring is first used, if no repository was chosen: with none, Data Protection would fall back to a local folder and
    /// there would be nothing to wrap, so the call is refused instead of leaving the keys unprotected without a word.
    /// </exception>
    public static IDataProtectionBuilder ProtectKeyRing(this IDataProtectionBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var services = builder.Services;

        // A second call must not wrap the repository twice.
        if (services.Any(descriptor => descriptor.ServiceType == typeof(IKeyRingProtector)))
            return builder;

        services.AddOptions<KeyRingProtectionOptions>()
            .BindConfiguration(KeyRingProtectionOptions.SectionName)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<KeyRingProtectionOptions>, KeyRingProtectionOptionsValidator>();
        services.AddSingleton<IKeyRingProtector>(provider => CreateProtector(
            provider.GetRequiredService<IOptions<KeyRingProtectionOptions>>().Value,
            provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(KeyRingProtectionExtensions).FullName!)));

        services.AddOptions<KeyManagementOptions>()
            .PostConfigure<IServiceProvider, ILoggerFactory>((options, provider, loggerFactory) =>
            {
                if (options.XmlRepository is not { } inner)
                {
                    throw new InvalidOperationException(
                        "The Data Protection key ring has no repository to protect. Choose where the keys are stored (for example PersistKeysToDbContext) before ProtectKeyRing().");
                }

                options.XmlRepository = new ProtectedXmlRepository(
                    inner,
                    () => provider.GetRequiredService<IKeyRingProtector>(),
                    loggerFactory.CreateLogger<ProtectedXmlRepository>());
            });

        return builder;
    }

    private static IKeyRingProtector CreateProtector(KeyRingProtectionOptions options, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(options.KeyEncryptionKey))
        {
            // The startup check allows this in Development only.
            logger.LogWarning(
                "{Setting} is not set, so the Data Protection key ring is stored unprotected. This is allowed in the Development environment only.",
                KeyRingProtectionOptions.KeySettingName);
            return new UnprotectedKeyRing();
        }

        // The options were validated when they were read, so these parse. The checks stay, because a key that did not parse must never become
        // a protector that quietly uses something else.
        if (!KeyRingKeyParser.TryParse(options.KeyEncryptionKey, out var current))
            throw new KeyRingProtectionException($"{KeyRingProtectionOptions.KeySettingName} is not a base64-encoded {KeyRingProtectionOptions.KeySizeBytes}-byte key.");

        var previous = new List<byte[]>();
        foreach (var text in options.PreviousKeyEncryptionKeys.Where(key => !string.IsNullOrWhiteSpace(key)))
        {
            if (!KeyRingKeyParser.TryParse(text, out var key))
                throw new KeyRingProtectionException($"{KeyRingProtectionOptions.PreviousKeysSettingName} holds a value that is not a base64-encoded {KeyRingProtectionOptions.KeySizeBytes}-byte key.");

            previous.Add(key);
        }

        return new AesGcmKeyRingProtector(current, previous);
    }
}
