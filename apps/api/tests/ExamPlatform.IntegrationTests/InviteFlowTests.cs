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

    [Fact]
    public async Task CreateInvite_WithoutAuth_ReturnsUnauthorized()
    {
        using var client = factory.CreateClient();

        var request = new CreateInviteRequest(Guid.NewGuid(), Guid.NewGuid(), "candidate@example.com", Guid.NewGuid());
        var response = await client.PostAsJsonAsync("/v1/invites", request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
