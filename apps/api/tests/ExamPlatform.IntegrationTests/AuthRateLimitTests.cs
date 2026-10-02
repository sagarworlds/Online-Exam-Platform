using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// An <see cref="ApiFactory"/> whose Identity rate-limit policies are low enough to exceed
/// in a test. The global limit stays at the test-wide value, so a 429 here can only come
/// from a named policy.
/// </summary>
public sealed class LowAuthRateLimitApiFactory : ApiFactory
{
    /// <summary>The permits each client IP gets per window under every Identity policy.</summary>
    public const int PermitLimit = 2;

    /// <inheritdoc />
    protected override IReadOnlyDictionary<string, string?> AdditionalConfiguration
    {
        get
        {
            var permits = PermitLimit.ToString(CultureInfo.InvariantCulture);
            return new Dictionary<string, string?>(base.AdditionalConfiguration)
            {
                ["Identity:RateLimits:OtpRequest:PermitLimit"] = permits,
                ["Identity:RateLimits:OtpVerify:PermitLimit"] = permits,
                ["Identity:RateLimits:PasswordLogin:PermitLimit"] = permits,
                ["Identity:RateLimits:PasswordReset:PermitLimit"] = permits,
            };
        }
    }
}

/// <summary>
/// Proves each sensitive Identity route is behind its named per-IP policy (NFR-5) and that
/// a rejection is the same structured 429 as the global limiter's. Each test uses its own
/// policy, because the factory (and so every policy's counter) is shared by the class.
/// </summary>
public sealed class AuthRateLimitTests(LowAuthRateLimitApiFactory factory) : IClassFixture<LowAuthRateLimitApiFactory>
{
    [Fact]
    public async Task OtpRequest_BeyondPolicyLimit_Returns429ProblemDetailsWithRetryAfter()
    {
        using var client = factory.CreateClient();
        var destination = $"limit-{Guid.NewGuid():N}@tests.local";

        // /register sends an OTP too, so it must draw on the same budget as /otp/request.
        var first = await client.PostAsJsonAsync("/v1/auth/otp/request", new { channel = "Email", destination });
        var second = await client.PostAsJsonAsync("/v1/auth/register", new
        {
            email = destination,
            dateOfBirth = "2000-01-01",
            displayName = "Rate Limit",
            otpChannel = "Email",
        });
        var rejected = await client.PostAsJsonAsync("/v1/auth/otp/request", new { channel = "Email", destination });

        Assert.NotEqual(HttpStatusCode.TooManyRequests, first.StatusCode);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.NotNull(rejected.Headers.RetryAfter?.Delta);
        var problem = await rejected.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("rate_limited", problem.GetProperty("title").GetString());
    }

    [Fact]
    public async Task OtpVerify_BeyondPolicyLimit_Returns429()
    {
        using var client = factory.CreateClient();
        var body = new { otpChallengeId = Guid.NewGuid(), code = "000000" };

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i <= LowAuthRateLimitApiFactory.PermitLimit; i++)
        {
            statuses.Add((await client.PostAsJsonAsync("/v1/auth/otp/verify", body)).StatusCode);
        }

        Assert.All(statuses.Take(LowAuthRateLimitApiFactory.PermitLimit), status => Assert.NotEqual(HttpStatusCode.TooManyRequests, status));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);
    }

    [Fact]
    public async Task PasswordLogin_BeyondPolicyLimit_Returns429()
    {
        using var client = factory.CreateClient();
        var body = new { email = $"nobody-{Guid.NewGuid():N}@tests.local", password = "not-the-password" };

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i <= LowAuthRateLimitApiFactory.PermitLimit; i++)
        {
            statuses.Add((await client.PostAsJsonAsync("/v1/auth/login", body)).StatusCode);
        }

        // A wrong password is refused with 401, not throttled, until the budget is spent.
        Assert.All(statuses.Take(LowAuthRateLimitApiFactory.PermitLimit), status => Assert.Equal(HttpStatusCode.Unauthorized, status));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);
    }

    [Fact]
    public async Task PasswordReset_RequestAndRedeem_ShareOneBudget()
    {
        using var client = factory.CreateClient();
        var email = $"nobody-{Guid.NewGuid():N}@tests.local";

        var request = await client.PostAsJsonAsync("/v1/auth/password-reset/request", new { email });
        var redeem = await client.PostAsJsonAsync(
            "/v1/auth/password-reset/reset",
            new { passwordResetTokenId = Guid.NewGuid(), token = "guess", newPassword = "correct horse battery staple" });
        var rejected = await client.PostAsJsonAsync("/v1/auth/password-reset/request", new { email });

        Assert.NotEqual(HttpStatusCode.TooManyRequests, request.StatusCode);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, redeem.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    }

    [Fact]
    public async Task HealthEndpoint_IsNotAffectedByAuthPolicies()
    {
        using var client = factory.CreateClient();

        for (var i = 0; i < LowAuthRateLimitApiFactory.PermitLimit * 3; i++)
        {
            var response = await client.GetAsync("/v1/health");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}
