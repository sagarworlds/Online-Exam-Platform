using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using ExamPlatform.Modules.Identity.Domain;
using ExamPlatform.Modules.Identity.Infrastructure;
using ExamPlatform.SharedKernel.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// Pins down what <see cref="TestSessions"/> promises every other suite: a token for a
/// persisted, live session that carries the seeded role's real claims and that the
/// running API accepts. Every suite that authenticates rests on this helper, so a
/// regression here (e.g. a token with no <c>perm</c> claims) must fail loudly here
/// rather than as a confusing 401/403 somewhere else.
/// </summary>
public sealed class TestSessionsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task SignInAsAsync_ReturnsATokenForAPersistedLiveSessionCarryingTheSeededRolesClaims()
    {
        var signedIn = await factory.SignInAsAsync("SuperAdmin");

        var token = new JwtSecurityTokenHandler().ReadJwtToken(signedIn.AccessToken);
        Assert.Equal(signedIn.UserId.ToString(), token.Subject);
        Assert.Equal(signedIn.SessionId.ToString(), Assert.Single(token.Claims, c => c.Type == "sid").Value);
        Assert.Contains(token.Claims, c => c.Type == ClaimTypes.Role && c.Value == "SuperAdmin");
        var permissions = token.Claims.Where(c => c.Type == "perm").Select(c => c.Value).ToHashSet();
        Assert.Superset(new HashSet<string> { "admin.audit.read", "consent.manage", "identity.role.assign" }, permissions);

        using (var scope = factory.Services.CreateScope())
        {
            var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var nowUtc = scope.ServiceProvider.GetRequiredService<Clock>().UtcNow;
            var session = await identityDb.Set<UserSession>().SingleAsync(s => s.Id == signedIn.SessionId);
            Assert.Equal(signedIn.UserId, session.UserId);
            Assert.True(session.IsActive(nowUtc));
        }

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", signedIn.AccessToken);

        var profile = await client.GetFromJsonAsync<JsonElement>("/v1/me/profile");
        Assert.Equal(signedIn.Email, profile.GetProperty("email").GetString());
        Assert.Equal(nameof(UserStatus.Active), profile.GetProperty("status").GetString());

        // A permission-gated endpoint, so the 'perm' claims are proven to satisfy the
        // real permission policy, not just to be present in the token.
        var auditResponse = await client.GetAsync("/v1/admin/audit-logs");
        Assert.Equal(HttpStatusCode.OK, auditResponse.StatusCode);
    }

    [Fact]
    public async Task SignInAsAsync_WithAPassword_StoresAHashThatPasswordLoginAccepts()
    {
        const string password = "integration-test-password";
        var signedIn = await factory.SignInAsAsync("Candidate", password: password);
        using var client = factory.CreateClient();

        var loginResponse = await client.PostAsJsonAsync("/v1/auth/login", new { email = signedIn.Email, password });

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var auth = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(auth.GetProperty("requiresTwoFactor").GetBoolean());
        Assert.False(string.IsNullOrEmpty(auth.GetProperty("accessToken").GetString()));
    }

    [Fact]
    public async Task SignInAsAsync_WithARoleThatIsNotSeeded_Throws()
    {
        // "Admin" is the role the old hand-signed tokens claimed; it was never seeded.
        await Assert.ThrowsAsync<InvalidOperationException>(() => factory.SignInAsAsync("Admin"));
    }
}
