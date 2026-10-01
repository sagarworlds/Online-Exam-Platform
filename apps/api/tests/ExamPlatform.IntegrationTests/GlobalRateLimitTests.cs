using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExamPlatform.Api.RateLimiting;
using Microsoft.Extensions.Options;

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

/// <summary>
/// An <see cref="ApiFactory"/> configured with a global permit limit of zero, which the
/// Host's start-time validation must refuse.
/// </summary>
public sealed class ZeroGlobalRateLimitApiFactory : ApiFactory
{
    /// <inheritdoc />
    protected override IReadOnlyDictionary<string, string?> AdditionalConfiguration =>
        new Dictionary<string, string?>(base.AdditionalConfiguration)
        {
            ["RateLimiting:Global:PermitLimit"] = "0",
        };
}

/// <summary>
/// Proves a non-positive global limit fails the boot, because Program.cs validates
/// <see cref="GlobalRateLimitOptions"/> on start, instead of surfacing as a 500 when the
/// first request builds the limiter.
/// </summary>
public sealed class GlobalRateLimitValidationTests(ZeroGlobalRateLimitApiFactory factory) : IClassFixture<ZeroGlobalRateLimitApiFactory>
{
    [Fact]
    public void NonPositivePermitLimit_FailsStartup()
    {
        var failure = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        // The host may surface the validation failure directly or wrapped (e.g. in an
        // AggregateException), so look for it anywhere in the exception chain.
        var validationFailure = SelfAndInnerExceptions(failure).OfType<OptionsValidationException>().FirstOrDefault();
        Assert.True(validationFailure is not null, $"Expected an OptionsValidationException, got: {failure}");
        Assert.Equal(typeof(GlobalRateLimitOptions), validationFailure.OptionsType);
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
