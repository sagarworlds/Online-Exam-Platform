using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Net.Http.Headers;
using ExamPlatform.Modules.Invite.Endpoints;

namespace ExamPlatform.IntegrationTests;

public class InviteFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task CreateInvite_WithValidData_ReturnsCreatedResponse()
    {
        using var client = factory.CreateClient();
        var token = TestJwtTokenBuilder.GenerateAdminToken();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var examId = Guid.NewGuid();
        var batchMemberId = Guid.NewGuid();
        var createdBy = Guid.NewGuid();
        var request = new CreateInviteRequest(
            examId,
            batchMemberId,
            "candidate@example.com",
            createdBy);

        var response = await client.PostAsJsonAsync("/v1/invites", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<dynamic>();
        Assert.NotNull(dto);
    }

    [Fact]
    public async Task GenerateInviteCode_WithValidData_ReturnsCreatedResponse()
    {
        using var client = factory.CreateClient();
        var token = TestJwtTokenBuilder.GenerateAdminToken();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var inviteRequest = new CreateInviteRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "candidate@example.com",
            Guid.NewGuid());
        var inviteResponse = await client.PostAsJsonAsync("/v1/invites", inviteRequest);
        inviteResponse.EnsureSuccessStatusCode();
        var invite = await inviteResponse.Content.ReadFromJsonAsync<JsonElement>();
        var inviteId = invite.GetProperty("id").GetGuid();

        var codeRequest = new GenerateInviteCodeRequest(72);
        var codeResponse = await client.PostAsJsonAsync($"/v1/invites/{inviteId}/codes", codeRequest);

        Assert.Equal(HttpStatusCode.Created, codeResponse.StatusCode);
    }

    // Accepting looks the code up among the invite's existing codes, so this only passes if the
    // invite is reloaded *with* its codes on the second request.
    [Fact]
    public async Task AcceptInvite_WithGeneratedCode_ReturnsNoContent()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtTokenBuilder.GenerateAdminToken());

        var inviteResponse = await client.PostAsJsonAsync(
            "/v1/invites",
            new CreateInviteRequest(Guid.NewGuid(), Guid.NewGuid(), "candidate@example.com", Guid.NewGuid()));
        inviteResponse.EnsureSuccessStatusCode();
        var inviteId = (await inviteResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var codeResponse = await client.PostAsJsonAsync($"/v1/invites/{inviteId}/codes", new GenerateInviteCodeRequest(72));
        codeResponse.EnsureSuccessStatusCode();
        var codeId = (await codeResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var accept = await client.PostAsJsonAsync($"/v1/invites/{inviteId}/accept", new AcceptInviteRequest(codeId));

        Assert.Equal(HttpStatusCode.NoContent, accept.StatusCode);
    }

    [Fact]
    public async Task CreateInvite_WithoutAuth_ReturnsUnauthorized()
    {
        using var client = factory.CreateClient();

        var request = new CreateInviteRequest(Guid.NewGuid(), Guid.NewGuid(), "candidate@example.com", Guid.NewGuid());
        var response = await client.PostAsJsonAsync("/v1/invites", request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
