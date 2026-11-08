using System.Net;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// The site and the API are different origins in production, and a call carrying the Authorization header or a JSON body makes the browser
/// send a preflight first. A browser keeps the answer only when the response says for how long, and otherwise asks again after 5 s, which
/// would double the requests of every candidate in an exam (NFR-1). This pins that the answer is kept for two hours, and that the policy
/// still turns away an origin it does not list.
/// </summary>
public sealed class CorsPreflightTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string AllowedOrigin = "http://localhost:4200";

    [Fact]
    public async Task APreflightFromAnAllowedOrigin_IsAnsweredWithAMaxAgeOfTwoHours()
    {
        using var client = factory.CreateClient();
        using var request = Preflight(AllowedOrigin);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(AllowedOrigin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Equal("7200", Assert.Single(response.Headers.GetValues("Access-Control-Max-Age")));
    }

    [Fact]
    public async Task APreflightFromAnOriginThatIsNotListed_IsNotGivenTheOriginBack()
    {
        using var client = factory.CreateClient();
        using var request = Preflight("https://elsewhere.example");

        var response = await client.SendAsync(request);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    private static HttpRequestMessage Preflight(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/v1/me/attempts/00000000-0000-0000-0000-000000000000");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "PUT");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");
        return request;
    }
}
