using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ExamPlatform.Modules.Batch.Endpoints;

namespace ExamPlatform.IntegrationTests;

public class BatchFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task CreateBatch_WithValidData_ReturnsCreatedResponse()
    {
        using var client = AuthorizedClient(await factory.SignInAsAsync("SuperAdmin"));

        var request = new CreateBatchRequest(
            Guid.NewGuid(),
            "Batch A",
            "First batch",
            50);

        var response = await client.PostAsJsonAsync("/v1/batches", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<dynamic>();
        Assert.NotNull(dto);
    }

    [Fact]
    public async Task CreateBatch_WithSpoofedCreatedByInBody_AttributesCreatorFromTokenSubject()
    {
        var caller = await factory.SignInAsAsync("SuperAdmin");
        using var client = AuthorizedClient(caller);

        // An older client (or an attacker) may still send createdBy; it must be ignored.
        var spoofedCreator = Guid.NewGuid();
        var response = await client.PostAsJsonAsync("/v1/batches", new
        {
            examId = Guid.NewGuid(),
            name = "Spoofed Creator Batch",
            description = (string?)null,
            maxMembers = 10,
            createdBy = spoofedCreator,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var batch = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(caller.UserId, batch.GetProperty("createdBy").GetGuid());
        Assert.NotEqual(spoofedCreator, batch.GetProperty("createdBy").GetGuid());
    }

    [Fact]
    public async Task AddBatchMember_WithValidData_ReturnsNoContent()
    {
        using var client = AuthorizedClient(await factory.SignInAsAsync("SuperAdmin"));

        var createRequest = new CreateBatchRequest(Guid.NewGuid(), "Test Batch", null, 50);
        var createResponse = await client.PostAsJsonAsync("/v1/batches", createRequest);
        createResponse.EnsureSuccessStatusCode();
        var batch = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var batchId = batch.GetProperty("id").GetGuid();

        var memberRequest = new AddBatchMemberRequest("candidate@example.com", "+91-9876543210");
        var memberResponse = await client.PostAsJsonAsync($"/v1/batches/{batchId}/members", memberRequest);

        Assert.Equal(HttpStatusCode.NoContent, memberResponse.StatusCode);
    }

    [Fact]
    public async Task CreateBatch_WithoutAuth_ReturnsUnauthorized()
    {
        using var client = factory.CreateClient();

        var request = new CreateBatchRequest(Guid.NewGuid(), "Test Batch", null, 50);
        var response = await client.PostAsJsonAsync("/v1/batches", request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private HttpClient AuthorizedClient(SignedInTestUser caller)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", caller.AccessToken);
        return client;
    }
}
