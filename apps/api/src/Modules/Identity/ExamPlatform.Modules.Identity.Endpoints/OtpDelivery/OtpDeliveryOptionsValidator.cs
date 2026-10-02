using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ExamPlatform.Modules.Identity.Endpoints.OtpDelivery;

/// <summary>
/// Refuses, at startup, an <see cref="OtpDeliveryOptions"/> that names no known provider,
/// or that names <see cref="OtpDeliveryOptions.DevelopmentLog"/> outside the Development
/// environment, so codes and reset links can never end up in a production log (NFR-6).
/// Registered with <c>ValidateOnStart</c>, so a misconfigured host fails to boot instead
/// of failing on the first sign-in.
/// </summary>
/// <remarks>
/// Because no real provider exists yet (FR-39), every non-Development host fails this check.
/// A <c>WebApplicationFactory</c> test that boots the Host in another environment must
/// therefore, in <c>ConfigureTestServices</c>, remove the
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
internal sealed class OtpDeliveryOptionsValidator(IHostEnvironment environment) : IValidateOptions<OtpDeliveryOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, OtpDeliveryOptions options)
    {
        if (options.Provider != OtpDeliveryOptions.DevelopmentLog)
        {
            return ValidateOptionsResult.Fail(
                $"No real IOtpSender adapter is configured ({OtpDeliveryOptions.SectionName}:Provider is "
                + $"'{options.Provider}'); real email/SMS delivery arrives with the Notifications module (FR-39). "
                + $"Only '{OtpDeliveryOptions.DevelopmentLog}' exists today, and only for the Development environment.");
        }

        if (!environment.IsDevelopment())
        {
            return ValidateOptionsResult.Fail(
                $"{OtpDeliveryOptions.SectionName}:Provider '{OtpDeliveryOptions.DevelopmentLog}' writes one-time codes "
                + $"and reset links to the log, so it is allowed only in Development, not in '{environment.EnvironmentName}'.");
        }

        return ValidateOptionsResult.Success;
    }
}
