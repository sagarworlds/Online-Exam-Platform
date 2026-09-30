using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// An <see cref="ApiFactory"/> whose global rate limit is low enough to exceed in a test.
/// </summary>
public sealed class LowGlobalRateLimitApiFactory : ApiFactory
{
    /// <summary>The permits each client IP gets per window under this factory.</summary>
    public const int PermitLimit = 2;

    /// <inheritdoc />
    protected override IReadOnlyDictionary<string, string?> AdditionalConfiguration =>
        new Dictionary<string, string?>(base.AdditionalConfiguration)
        {
            ["RateLimiting:Global:PermitLimit"] = PermitLimit.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
}

/// <summary>
/// Proves the global per-IP limiter reads its limit from configuration and rejects
/// excess requests with a structured 429 (NFR-5), not an empty body.
/// </summary>
public sealed class GlobalRateLimitTests(LowGlobalRateLimitApiFactory factory) : IClassFixture<LowGlobalRateLimitApiFactory>
{
    [Fact]
    public async Task RequestsBeyondTheGlobalLimit_Return429ProblemDetailsWithRetryAfter()
    {
        using var client = factory.CreateClient();

        for (var i = 0; i < LowGlobalRateLimitApiFactory.PermitLimit; i++)
        {
            var allowed = await client.GetAsync("/v1/health");
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        var rejected = await client.GetAsync("/v1/health");

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.NotNull(rejected.Headers.RetryAfter?.Delta);
        Assert.True(rejected.Headers.RetryAfter!.Delta > TimeSpan.Zero);
        var problem = await rejected.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(429, problem.GetProperty("status").GetInt32());
        Assert.Equal("rate_limited", problem.GetProperty("title").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("detail").GetString()));
    }
}
