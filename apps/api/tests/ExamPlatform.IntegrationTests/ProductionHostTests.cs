using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// Pipeline behaviour that differs outside Development (NFR-5): HSTS on, the API
/// description and its UI off, and forwarded-header trust configured explicitly. Boots the
/// Host as Production without a database; none of these requests reach one.
/// </summary>
public sealed class ProductionHostTests
{
    private static readonly WebApplicationFactoryClientOptions HttpsClient = new()
    {
        BaseAddress = new Uri("https://api.example.com"),
    };

    [Fact]
    public async Task Production_SendsHstsOverHttps()
    {
        using var factory = new ProductionHostFactory(otpProvider: null, allowCapturingSender: true);
        using var client = factory.CreateClient(HttpsClient);

        var response = await client.GetAsync("/v1/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(
            response.Headers.Contains("Strict-Transport-Security"),
            "A deployed host must tell browsers to use HTTPS only.");
    }

    [Theory]
    [InlineData("/openapi/v1.json")]
    [InlineData("/scalar/v1")]
    public async Task Production_DoesNotServeTheApiDescriptionOrItsUi(string path)
    {
        using var factory = new ProductionHostFactory(otpProvider: null, allowCapturingSender: true);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("ForwardedHeaders:KnownProxies:0", "not-an-address")]
    [InlineData("ForwardedHeaders:KnownNetworks:0", "10.0.0.0/99")]
    public void MalformedForwardedHeadersTrust_FailsStartup(string key, string value)
    {
        using var factory = new ProductionHostFactory(
            otpProvider: null,
            allowCapturingSender: true,
            extraSettings: new Dictionary<string, string?> { [key] = value });

        var failure = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains(value, failure.ToString());
    }

    [Fact]
    public async Task Production_WithValidForwardedHeadersTrust_Boots()
    {
        using var factory = new ProductionHostFactory(
            otpProvider: null,
            allowCapturingSender: true,
            extraSettings: new Dictionary<string, string?>
            {
                ["ForwardedHeaders:KnownProxies:0"] = "10.1.2.3",
                ["ForwardedHeaders:KnownNetworks:0"] = "172.16.0.0/12",
            });
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/v1/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
