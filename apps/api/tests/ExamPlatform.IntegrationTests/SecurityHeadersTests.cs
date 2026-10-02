using System.Net;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// Proves API responses carry the defensive headers (NFR-5), on success and on error
/// paths, and that the Development-only API reference UI is not blanked out by them.
/// </summary>
public sealed class SecurityHeadersTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task HealthEndpoint_CarriesSecurityHeaders()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/v1/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertSecurityHeaders(response, expectContentSecurityPolicy: true);
    }

    [Fact]
    public async Task ErrorResponses_CarrySecurityHeaders()
    {
        using var client = factory.CreateClient();

        var unauthorized = await client.GetAsync("/v1/me/profile");
        var notFound = await client.GetAsync("/v1/no-such-route");
        var badRequest = await client.PostAsync(
            "/v1/auth/otp/request", new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badRequest.StatusCode);
        AssertSecurityHeaders(unauthorized, expectContentSecurityPolicy: true);
        AssertSecurityHeaders(notFound, expectContentSecurityPolicy: true);
        AssertSecurityHeaders(badRequest, expectContentSecurityPolicy: true);
    }

    [Fact]
    public async Task ApiReferenceUi_InDevelopment_IsNotUnderTheRestrictivePolicy()
    {
        using var client = factory.CreateClient();

        var document = await client.GetAsync("/openapi/v1.json");
        var reference = await client.GetAsync("/scalar/v1");

        Assert.Equal(HttpStatusCode.OK, document.StatusCode);
        Assert.Equal(HttpStatusCode.OK, reference.StatusCode);
        AssertSecurityHeaders(document, expectContentSecurityPolicy: true);

        // The UI is an HTML page with its own script; default-src 'none' would blank it.
        AssertSecurityHeaders(reference, expectContentSecurityPolicy: false);
        Assert.False(reference.Headers.Contains("Strict-Transport-Security"));
    }

    private static void AssertSecurityHeaders(HttpResponseMessage response, bool expectContentSecurityPolicy)
    {
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));

        if (expectContentSecurityPolicy)
        {
            Assert.Equal(
                "default-src 'none'; frame-ancestors 'none'",
                Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
        }
        else
        {
            Assert.False(response.Headers.Contains("Content-Security-Policy"));
        }
    }
}
