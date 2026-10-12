using Microsoft.Extensions.Options;

namespace ExamPlatform.SharedKernel.Infrastructure.Sms;

/// <summary>
/// Refuses, at startup, an <see cref="SmsOptions"/> that is switched on without what an SMS needs: a provider the platform has an adapter
/// for, the sender ID registered with the DLT platform, and a DLT template ID for each message template (FR-39). Indian rules require those
/// registrations before the first send, so a host switched on without them would fail on its first message instead of at boot.
/// </summary>
/// <remarks>
/// Switched off, nothing is demanded: the settings can be put in place first, and the switch turned off again in an emergency without the host
/// refusing to start. Turning the switch on is what checks them. Until a provider adapter is built, the provider list is empty and a switched-on
/// host is refused, which keeps SMS off in practice until the owner has chosen a provider (issue #234).
/// </remarks>
/// <param name="builtInProviders">The names of the providers the platform has an adapter for. Empty until one is built.</param>
public sealed class SmsOptionsValidator(IEnumerable<string> builtInProviders) : IValidateOptions<SmsOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, SmsOptions options)
    {
        if (!options.IsEnabled)
        {
            return ValidateOptionsResult.Success;
        }

        // A provider that is named but has no adapter is refused on its own: the other settings cannot make it send.
        if (!string.IsNullOrWhiteSpace(options.Provider)
            && !builtInProviders.Contains(options.Provider, StringComparer.OrdinalIgnoreCase))
        {
            return ValidateOptionsResult.Fail(
                $"{SmsOptions.SectionName}:Enabled is true, but no SMS provider named '{options.Provider}' is built into this platform. "
                + $"Leave {SmsOptions.SectionName}:Enabled unset until a provider is chosen and built (FR-39).");
        }

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(options.Provider))
        {
            missing.Add($"{SmsOptions.SectionName}:Provider");
        }

        if (string.IsNullOrWhiteSpace(options.SenderId))
        {
            missing.Add($"{SmsOptions.SectionName}:SenderId");
        }

        if (options.Templates.Count == 0 || options.Templates.Values.Any(string.IsNullOrWhiteSpace))
        {
            missing.Add($"{SmsOptions.SectionName}:Templates (each template needs its DLT template ID)");
        }

        return missing.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{SmsOptions.SectionName}:Enabled is true, but {string.Join(", ", missing)} "
                + (missing.Count == 1 ? "is" : "are") + " not set.");
    }
}
