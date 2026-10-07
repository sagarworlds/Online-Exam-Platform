using System.Net;
using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Endpoints.OtpDelivery;
using ExamPlatform.Modules.Identity.Endpoints.RateLimiting;
using ExamPlatform.Modules.Invite.Infrastructure.WhatsApp;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// Boots the Host in a non-Development environment with no Postgres container: startup
/// validation runs before any request, so these tests need no database. A Production host
/// skips the migrate-and-seed step, and the connection string is never opened.
/// </summary>
/// <param name="otpProvider">The value for <c>Identity:OtpDelivery:Provider</c>, or null to leave it unset.</param>
/// <param name="allowCapturingSender">
/// Whether to apply the override documented on <c>OtpDeliveryOptionsValidator</c>: drop the
/// options validator and register <see cref="CapturingOtpSender"/>.
/// </param>
/// <param name="extraSettings">More configuration to layer on, e.g. a <c>ForwardedHeaders</c> list.</param>
public sealed class ProductionHostFactory(
    string? otpProvider,
    bool allowCapturingSender = false,
    IReadOnlyDictionary<string, string?>? extraSettings = null) : WebApplicationFactory<Program>
{
    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");

        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = "Host=localhost;Port=1;Database=unused;Username=unused;Password=unused",
            ["Jwt:SigningKey"] = "integration-test-only-signing-key-at-least-32-bytes",
            ["Jwt:Issuer"] = "exam-platform-tests",
            ["Jwt:Audience"] = "exam-platform-tests-clients",
        };
        if (otpProvider is not null)
        {
            settings["Identity:OtpDelivery:Provider"] = otpProvider;
        }

        foreach (var (key, value) in extraSettings ?? new Dictionary<string, string?>())
        {
            settings[key] = value;
        }

        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));

        builder.ConfigureTestServices(TestRemoteIpStartupFilter.Register);

        if (allowCapturingSender)
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IValidateOptions<OtpDeliveryOptions>>();
                services.AddSingleton<IOtpSender>(new CapturingOtpSender());
            });
        }
    }
}

/// <summary>
/// Proves a host that would write one-time codes to its log, or that has no way to deliver
/// them, refuses to start instead of failing on the first sign-in or leaking codes (NFR-6),
/// and that a non-positive Identity rate limit fails the boot rather than the first request
/// (NFR-5).
/// </summary>
public sealed class StartupGuardTests
{
    [Fact]
    public void NonDevelopment_WithDevelopmentLogSender_FailsAtStartup()
    {
        using var factory = new ProductionHostFactory(OtpDeliveryOptions.DevelopmentLog);

        var failure = AssertStartupFails<OtpDeliveryOptions>(factory);

        Assert.Contains(OtpDeliveryOptions.SectionName, failure.Message);
        Assert.Contains("Production", failure.Message);
    }

    [Fact]
    public void NonDevelopment_WithoutOtpProvider_FailsAtStartup()
    {
        using var factory = new ProductionHostFactory(otpProvider: null);

        var failure = AssertStartupFails<OtpDeliveryOptions>(factory);

        Assert.Contains(OtpDeliveryOptions.SectionName, failure.Message);
        Assert.Contains("FR-39", failure.Message);
    }

    [Fact]
    public void NonDevelopment_WithUnknownOtpProvider_FailsAtStartup()
    {
        using var factory = new ProductionHostFactory("Carrier-Pigeon");

        var failure = AssertStartupFails<OtpDeliveryOptions>(factory);

        Assert.Contains("Carrier-Pigeon", failure.Message);
    }

    [Fact]
    public async Task NonDevelopment_WithSmtpSender_Boots()
    {
        using var factory = new ProductionHostFactory(OtpDeliveryOptions.Smtp);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/v1/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static ProductionHostFactory SmtpHostWith(params (string Key, string? Value)[] settings) =>
        new(
            OtpDeliveryOptions.Smtp,
            extraSettings: settings.ToDictionary(setting => setting.Key, setting => setting.Value));

    [Fact]
    public void NonDevelopment_WithWhatsAppForPhonesButNothingConfiguredForWhatsApp_FailsAtStartupNamingWhatIsMissing()
    {
        // Told to send codes over WhatsApp with no way to, the host must stop here, not fail the first phone sign-in.
        using var factory = SmtpHostWith(("Identity:OtpDelivery:PhoneProvider", OtpDeliveryOptions.WhatsApp));

        var failure = AssertStartupFails<OtpDeliveryOptions>(factory);

        Assert.Contains("WhatsApp:AccessToken, WhatsApp:PhoneNumberId, WhatsApp:OtpTemplateName are not set", failure.Message);
    }

    [Fact]
    public void NonDevelopment_WithWhatsAppForPhonesButNoCodeTemplate_FailsAtStartupNamingOnlyThat()
    {
        using var factory = SmtpHostWith(
            ("Identity:OtpDelivery:PhoneProvider", OtpDeliveryOptions.WhatsApp),
            ("WhatsApp:AccessToken", "token"),
            ("WhatsApp:PhoneNumberId", "1234567890"));

        var failure = AssertStartupFails<OtpDeliveryOptions>(factory);

        Assert.Contains("WhatsApp:OtpTemplateName is not set", failure.Message);
        Assert.DoesNotContain("AccessToken", failure.Message);
    }

    [Fact]
    public void NonDevelopment_WithAnUnknownPhoneProvider_FailsAtStartup()
    {
        using var factory = SmtpHostWith(("Identity:OtpDelivery:PhoneProvider", "Carrier-Pigeon"));

        var failure = AssertStartupFails<OtpDeliveryOptions>(factory);

        Assert.Contains("Carrier-Pigeon", failure.Message);
    }

    [Fact]
    public async Task NonDevelopment_WithWhatsAppForPhonesAndItsSettings_Boots()
    {
        using var factory = SmtpHostWith(
            ("Identity:OtpDelivery:PhoneProvider", OtpDeliveryOptions.WhatsApp),
            ("WhatsApp:AccessToken", "token"),
            ("WhatsApp:PhoneNumberId", "1234567890"),
            ("WhatsApp:OtpTemplateName", "exam_login_code"));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/v1/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void NonDevelopment_WithAnInviteTemplateButNothingConfiguredForWhatsApp_FailsAtStartupNamingWhatIsMissing()
    {
        // Told to send invitations on WhatsApp with no way to, the host must stop here, not skip WhatsApp on every invitation.
        using var factory = SmtpHostWith(("Invite:WhatsApp:TemplateName", "exam_invitation"));

        var failure = AssertStartupFails<InviteWhatsAppOptions>(factory);

        Assert.Contains("Invite:WhatsApp:TemplateName is set, but WhatsApp:AccessToken, WhatsApp:PhoneNumberId are not set", failure.Message);
    }

    [Fact]
    public async Task NonDevelopment_WithAnInviteTemplateAndWhatsAppConfigured_Boots()
    {
        using var factory = SmtpHostWith(
            ("Invite:WhatsApp:TemplateName", "exam_invitation"),
            ("WhatsApp:AccessToken", "token"),
            ("WhatsApp:PhoneNumberId", "1234567890"));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/v1/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task NonDevelopment_WithABlankInviteTemplate_BootsAsIfItWereUnset()
    {
        using var factory = SmtpHostWith(("Invite:WhatsApp:TemplateName", ""));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/v1/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task NonDevelopment_WithABlankPhoneProvider_BootsAsIfItWereUnset()
    {
        // A platform that lets an operator clear a variable can hand over an empty string rather than nothing.
        using var factory = SmtpHostWith(("Identity:OtpDelivery:PhoneProvider", ""));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/v1/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task NonDevelopment_WithValidatorOverriddenAndSenderReplaced_Boots()
    {
        // The recipe documented on OtpDeliveryOptionsValidator for tests that need a
        // non-Development host: it must keep working, or that documentation is a lie.
        using var factory = new ProductionHostFactory(otpProvider: null, allowCapturingSender: true);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/v1/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("OtpRequest", "PermitLimit", "0")]
    [InlineData("OtpVerify", "WindowSeconds", "0")]
    [InlineData("PasswordLogin", "PermitLimit", "-1")]
    [InlineData("PasswordReset", "WindowSeconds", "-1")]
    public void NonPositiveIdentityRateLimit_FailsAtStartupNamingThePolicy(string policy, string setting, string value)
    {
        // A non-positive limit or window is a misconfiguration: it must stop the boot, not turn
        // into every request being rejected or a 500 when the first request builds the limiter.
        using var factory = new ProductionHostFactory(
            otpProvider: null,
            allowCapturingSender: true,
            extraSettings: new Dictionary<string, string?> { [$"Identity:RateLimits:{policy}:{setting}"] = value });

        var failure = AssertStartupFails<IdentityRateLimitOptions>(factory);

        Assert.Contains($"Identity:RateLimits:{policy}", failure.Message);
    }

    /// <summary>How many times to try again when the framework loses the startup failure (see <see cref="AssertStartupFails{TOptions}"/>).</summary>
    private const int StartupFailureAttempts = 5;

    private static OptionsValidationException AssertStartupFails<TOptions>(ProductionHostFactory factory)
    {
        // WebApplicationFactory runs the host's entry point on another thread and, when startup throws, races to report it:
        // sometimes the caller sees the real OptionsValidationException, and sometimes only an ObjectDisposedException from the
        // already-disposed service provider, with the real one lost. Under a loaded CI machine the second happens now and then.
        // That outcome says nothing about the code under test, so it alone is retried; any other failure is judged at once.
        Exception failure;
        var attempt = 0;
        do
        {
            attempt++;
            failure = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        }
        while (attempt < StartupFailureAttempts && IsLostStartupFailure(failure));

        // The host may surface the validation failure directly or wrapped (e.g. in an
        // AggregateException), so look for it anywhere in the exception chain.
        var validationFailure = SelfAndInnerExceptions(failure).OfType<OptionsValidationException>().FirstOrDefault();
        Assert.True(validationFailure is not null, $"Expected an OptionsValidationException, got: {failure}");
        Assert.Equal(typeof(TOptions), validationFailure.OptionsType);
        return validationFailure;
    }

    private static bool IsLostStartupFailure(Exception failure)
    {
        var chain = SelfAndInnerExceptions(failure).ToList();
        return chain.OfType<ObjectDisposedException>().Any() && !chain.OfType<OptionsValidationException>().Any();
    }

    private static IEnumerable<Exception> SelfAndInnerExceptions(Exception exception)
    {
        yield return exception;

        var inner = exception is AggregateException aggregate
            ? aggregate.InnerExceptions
            : exception.InnerException is { } single ? [single] : [];

        foreach (var nested in inner.SelectMany(SelfAndInnerExceptions))
        {
            yield return nested;
        }
    }
}
