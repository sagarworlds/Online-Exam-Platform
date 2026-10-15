using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ExamPlatform.SharedKernel.Infrastructure.KeyRing;

/// <summary>
/// Refuses, at startup, a host that would keep the Data Protection key ring unprotected outside Development, and a key that is not a key
/// (NFR-5, #86). A missing or malformed key then stops the deployment with a message that says what to set, instead of storing the keys
/// beside the content they open.
/// </summary>
/// <remarks>
/// A Development host may leave the key blank, so a developer's database needs no secret. Nothing else may: a deployment that skipped the key
/// would otherwise work, silently, with the keys in the clear. A failed deploy is the better outcome, because the previous version keeps
/// serving until the setting is added. No message here ever contains a key, only the name of the setting.
/// </remarks>
/// <param name="environment">The host environment; only Development may leave the key unset.</param>
public sealed class KeyRingProtectionOptionsValidator(IHostEnvironment environment) : IValidateOptions<KeyRingProtectionOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, KeyRingProtectionOptions options)
    {
        var failures = new List<string>();
        var hasKey = !string.IsNullOrWhiteSpace(options.KeyEncryptionKey);

        if (!hasKey && !environment.IsDevelopment())
        {
            failures.Add(
                $"{KeyRingProtectionOptions.KeySettingName} is not set. Outside Development the Data Protection key ring has to be protected by a key "
                + "held outside the database (NFR-5), or a copy of the database would open the question content. "
                + "Set it to 32 random bytes in base64, for example the output of 'openssl rand -base64 32', and keep a copy of that key somewhere safe "
                + "and apart from the database: without it the question content cannot be decrypted.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (hasKey)
        {
            if (!KeyRingKeyParser.TryParse(options.KeyEncryptionKey, out var current))
                failures.Add(WrongForm(KeyRingProtectionOptions.KeySettingName));
            else
                ids.Add(KeyRingKeyParser.IdOf(current));
        }

        var previous = options.PreviousKeyEncryptionKeys ?? [];
        if (!hasKey && previous.Any(key => !string.IsNullOrWhiteSpace(key)))
            failures.Add($"{KeyRingProtectionOptions.PreviousKeysSettingName} is set, but {KeyRingProtectionOptions.KeySettingName} is not. The retired keys only open entries; a current key is needed to protect them.");

        for (var index = 0; index < previous.Length; index++)
        {
            if (string.IsNullOrWhiteSpace(previous[index]))
                continue;

            var setting = $"{KeyRingProtectionOptions.PreviousKeysSettingName}:{index}";
            if (!KeyRingKeyParser.TryParse(previous[index], out var retired))
                failures.Add(WrongForm(setting));
            else if (!ids.Add(KeyRingKeyParser.IdOf(retired)))
                failures.Add($"{setting} is the same key as the current key or another retired key. Each key may be listed once.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static string WrongForm(string setting) =>
        $"{setting} must be a base64-encoded key of exactly {KeyRingProtectionOptions.KeySizeBytes} bytes. 'openssl rand -base64 32' makes one.";
}
