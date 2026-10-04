using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// <c>GET /v1/admin/otp-codes</c>: a super administrator can read a candidate's unspent sign-in code
/// to help them in, the code stops being readable once used, and nobody else can ask.
/// </summary>
public sealed class OtpCodesEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task SuperAdmin_ReadsTheCodeACandidateIsWaitingFor_AndThatCodeSignsThemIn()
    {
        var candidate = await factory.SignInAsAsync("Candidate");
        using var anonymous = factory.CreateClient();
        var request = await anonymous.PostAsJsonAsync(
            "/v1/auth/otp/request", new { channel = "Email", destination = candidate.Email });
        request.EnsureSuccessStatusCode();
        var challengeId = (await request.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("otpChallengeId").GetGuid();
        using var admin = AuthorizedClient((await factory.SignInAsAsync("SuperAdmin")).AccessToken);

        var codes = await admin.GetFromJsonAsync<JsonElement>($"/v1/admin/otp-codes?destination={Uri.EscapeDataString(candidate.Email)}");

        var shown = Assert.Single(codes.EnumerateArray());
        Assert.Equal(challengeId, shown.GetProperty("challengeId").GetGuid());
        Assert.Equal("Login", shown.GetProperty("purpose").GetString());
        var code = shown.GetProperty("code").GetString()!;
        Assert.Equal(factory.OtpSender.GetLastCode(candidate.Email), code);

        // The code read off the portal is the one that signs the candidate in, and it is spent afterwards.
        var verify = await anonymous.PostAsJsonAsync("/v1/auth/otp/verify", new { otpChallengeId = challengeId, code });
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        var after = await admin.GetFromJsonAsync<JsonElement>($"/v1/admin/otp-codes?destination={Uri.EscapeDataString(candidate.Email)}");
        Assert.Empty(after.EnumerateArray());
    }

    [Fact]
    public async Task ARequestForAnUnknownDestination_ShowsNoCode()
    {
        var destination = $"nobody-{Guid.NewGuid():N}@tests.local";
        using var anonymous = factory.CreateClient();
        (await anonymous.PostAsJsonAsync("/v1/auth/otp/request", new { channel = "Email", destination })).EnsureSuccessStatusCode();
        using var admin = AuthorizedClient((await factory.SignInAsAsync("SuperAdmin")).AccessToken);

        var codes = await admin.GetFromJsonAsync<JsonElement>($"/v1/admin/otp-codes?destination={Uri.EscapeDataString(destination)}");

        // The decoy issued for an account that does not exist has no code anyone could read.
        Assert.Empty(codes.EnumerateArray());
    }

    [Theory]
    [InlineData("ExamAdmin")]
    [InlineData("InstituteTeacher")]
    [InlineData("Candidate")]
    public async Task WithoutTheOtpReadPermission_Returns403(string roleName)
    {
        using var client = AuthorizedClient((await factory.SignInAsAsync(roleName)).AccessToken);

        var response = await client.GetAsync("/v1/admin/otp-codes");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task WithoutAuth_Returns401()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/v1/admin/otp-codes");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private HttpClient AuthorizedClient(string accessToken)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }
}
