using System.Net;
using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Endpoints.OtpDelivery;
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
public sealed class ProductionHostFactory(string? otpProvider, bool allowCapturingSender = false) : WebApplicationFactory<Program>
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

        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));

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
/// them, refuses to start instead of failing on the first sign-in or leaking codes (NFR-6).
/// </summary>
public sealed class StartupGuardTests
{
    [Fact]
    public void NonDevelopment_WithDevelopmentLogSender_FailsAtStartup()
    {
        using var factory = new ProductionHostFactory(OtpDeliveryOptions.DevelopmentLog);

        var failure = AssertStartupFails(factory);

        Assert.Contains(OtpDeliveryOptions.SectionName, failure.Message);
        Assert.Contains("Production", failure.Message);
    }

    [Fact]
    public void NonDevelopment_WithoutOtpProvider_FailsAtStartup()
    {
        using var factory = new ProductionHostFactory(otpProvider: null);

        var failure = AssertStartupFails(factory);

        Assert.Contains(OtpDeliveryOptions.SectionName, failure.Message);
        Assert.Contains("FR-39", failure.Message);
    }

    [Fact]
    public void NonDevelopment_WithUnknownOtpProvider_FailsAtStartup()
    {
        using var factory = new ProductionHostFactory("Carrier-Pigeon");

        var failure = AssertStartupFails(factory);

        Assert.Contains("Carrier-Pigeon", failure.Message);
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

    private static OptionsValidationException AssertStartupFails(ProductionHostFactory factory)
    {
        var failure = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        // The host may surface the validation failure directly or wrapped (e.g. in an
        // AggregateException), so look for it anywhere in the exception chain.
        var validationFailure = SelfAndInnerExceptions(failure).OfType<OptionsValidationException>().FirstOrDefault();
        Assert.True(validationFailure is not null, $"Expected an OptionsValidationException, got: {failure}");
        Assert.Equal(typeof(OtpDeliveryOptions), validationFailure.OptionsType);
        return validationFailure;
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
