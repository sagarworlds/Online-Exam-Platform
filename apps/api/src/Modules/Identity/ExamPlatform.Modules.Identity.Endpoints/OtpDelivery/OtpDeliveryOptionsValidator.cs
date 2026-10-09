using ExamPlatform.SharedKernel.Infrastructure.WhatsApp;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ExamPlatform.Modules.Identity.Endpoints.OtpDelivery;

/// <summary>
/// Refuses, at startup, an <see cref="OtpDeliveryOptions"/> that names no known provider,
/// or that names <see cref="OtpDeliveryOptions.DevelopmentLog"/> outside the Development
/// environment, so codes and reset links can never end up in a production log (NFR-6).
/// It also refuses a <see cref="OtpDeliveryOptions.PhoneProvider"/> that is not WhatsApp, or is WhatsApp, with WhatsApp switched on
/// (<c>WhatsApp:Enabled</c>), without the settings WhatsApp needs, so a host that is told to send codes there cannot start and then
/// fail on the first sign-in.
/// Registered with <c>ValidateOnStart</c>, so a misconfigured host fails to boot instead
/// of failing on the first sign-in.
/// </summary>
/// <remarks>
/// A host with no e-mail provider named fails this check, because phone delivery alone is not a way to sign everyone in
/// (staff and anyone without a phone number need e-mail). A <c>WebApplicationFactory</c> test that boots the Host in another
/// environment must therefore, in <c>ConfigureTestServices</c>, remove the
/// <c>IValidateOptions&lt;OtpDeliveryOptions&gt;</c> registration and register its own
/// <c>IOtpSender</c> (the integration tests' <c>CapturingOtpSender</c>):
/// <code>
/// services.RemoveAll&lt;IValidateOptions&lt;OtpDeliveryOptions&gt;&gt;();
/// services.AddSingleton&lt;IOtpSender&gt;(new CapturingOtpSender());
/// </code>
/// The second registration matters as much as the first: without it, resolving
/// <c>IOtpSender</c> throws, because the module's factory only knows the providers this
/// validator accepts.
/// </remarks>
/// <param name="environment">The host environment, to tell Development from everything else.</param>
/// <param name="whatsApp">The WhatsApp settings, to check that a phone provider of WhatsApp has what it needs.</param>
internal sealed class OtpDeliveryOptionsValidator(IHostEnvironment environment, IOptions<WhatsAppOptions> whatsApp)
    : IValidateOptions<OtpDeliveryOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, OtpDeliveryOptions options)
    {
        var provider = ValidateProvider(options);
        return provider.Succeeded ? ValidatePhoneProvider(options) : provider;
    }

    private ValidateOptionsResult ValidateProvider(OtpDeliveryOptions options)
    {
        if (options.Provider == OtpDeliveryOptions.Smtp)
        {
            return ValidateOptionsResult.Success;
        }

        if (options.Provider != OtpDeliveryOptions.DevelopmentLog)
        {
            return ValidateOptionsResult.Fail(
                $"No real IOtpSender adapter is configured ({OtpDeliveryOptions.SectionName}:Provider is "
                + $"'{options.Provider}'); SMS delivery arrives with the Notifications module (FR-39). "
                + $"Only '{OtpDeliveryOptions.Smtp}' (e-mail) works outside Development, and "
                + $"'{OtpDeliveryOptions.DevelopmentLog}' only inside it. Codes for phone numbers go over WhatsApp when "
                + $"{OtpDeliveryOptions.SectionName}:PhoneProvider is '{OtpDeliveryOptions.WhatsApp}'.");
        }

        if (!environment.IsDevelopment())
        {
            return ValidateOptionsResult.Fail(
                $"{OtpDeliveryOptions.SectionName}:Provider '{OtpDeliveryOptions.DevelopmentLog}' writes one-time codes "
                + $"and reset links to the log, so it is allowed only in Development, not in '{environment.EnvironmentName}'.");
        }

        return ValidateOptionsResult.Success;
    }

    private ValidateOptionsResult ValidatePhoneProvider(OtpDeliveryOptions options)
    {
        // A blank value is "not set": a platform that lets an operator clear a variable may hand over an empty string.
        if (string.IsNullOrWhiteSpace(options.PhoneProvider))
        {
            return ValidateOptionsResult.Success;
        }

        if (options.PhoneProvider != OtpDeliveryOptions.WhatsApp)
        {
            return ValidateOptionsResult.Fail(
                $"{OtpDeliveryOptions.SectionName}:PhoneProvider is '{options.PhoneProvider}', but the only phone provider is "
                + $"'{OtpDeliveryOptions.WhatsApp}'. Leave it unset to send nothing to phone numbers.");
        }

        // Switched off, nothing is sent, so nothing else is demanded: the credentials can be put in place first, and the switch turned
        // off again in an emergency without the host refusing to start. Turning it on is what checks them.
        if (!whatsApp.Value.IsEnabled)
        {
            return ValidateOptionsResult.Success;
        }

        var missing = whatsApp.Value.MissingForOtp();
        return missing.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{OtpDeliveryOptions.SectionName}:PhoneProvider is '{OtpDeliveryOptions.WhatsApp}' and WhatsApp is switched on, but "
                + $"{string.Join(", ", missing.Select(setting => $"{WhatsAppOptions.SectionName}:{setting}"))} "
                + (missing.Count == 1 ? "is" : "are") + " not set.");
    }
}
