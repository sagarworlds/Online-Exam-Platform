using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ExamPlatform.Modules.Invite.Endpoints;
using ExamPlatform.Modules.Invite.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExamPlatform.IntegrationTests;

public class InviteFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task CreateInvite_WithValidData_ReturnsCreatedResponse()
    {
        using var client = AuthorizedClient(await factory.SignInAsAsync("SuperAdmin"));

        var request = new CreateInviteRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "candidate@example.com");

        var response = await client.PostAsJsonAsync("/v1/invites", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<dynamic>();
        Assert.NotNull(dto);
    }

    [Fact]
    public async Task CreateInvite_WithSpoofedCreatedByUserId_IsIgnored()
    {
        var caller = await factory.SignInAsAsync("SuperAdmin");
        using var client = AuthorizedClient(caller);

        // An older client (or an attacker) may still send createdByUserId; it must be ignored.
        var spoofedCreator = Guid.NewGuid();
        var response = await client.PostAsJsonAsync("/v1/invites", new
        {
            examId = Guid.NewGuid(),
            batchMemberId = Guid.NewGuid(),
            email = "candidate@example.com",
            createdByUserId = spoofedCreator,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var invite = await response.Content.ReadFromJsonAsync<JsonElement>();
        var inviteId = invite.GetProperty("id").GetGuid();

        // The invite DTO does not expose its creator, so read it back from the module's own store.
        using var scope = factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<InviteDbContext>()
            .Invites.AsNoTracking().SingleAsync(i => i.Id == inviteId);
        Assert.Equal(caller.UserId, stored.CreatedByUserId);
        Assert.NotEqual(spoofedCreator, stored.CreatedByUserId);
    }

    [Fact]
    public async Task GenerateInviteCode_WithValidData_ReturnsCreatedResponse()
    {
        using var client = AuthorizedClient(await factory.SignInAsAsync("SuperAdmin"));

        var inviteRequest = new CreateInviteRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "candidate@example.com");
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

        var request = new CreateInviteRequest(Guid.NewGuid(), Guid.NewGuid(), "candidate@example.com");
        var response = await client.PostAsJsonAsync("/v1/invites", request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private HttpClient AuthorizedClient(SignedInTestUser caller)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", caller.AccessToken);
        return client;
    }
}
