using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Net.Http.Headers;
using ExamPlatform.Modules.Batch.Endpoints;

namespace ExamPlatform.IntegrationTests;

public class BatchFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task CreateBatch_WithValidData_ReturnsCreatedResponse()
    {
        using var client = factory.CreateClient();
        var token = TestJwtTokenBuilder.GenerateAdminToken();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var examId = Guid.NewGuid();
        var createdBy = Guid.NewGuid();
        var request = new CreateBatchRequest(
            examId,
            "Batch A",
            "First batch",
            50,
            createdBy);

        var response = await client.PostAsJsonAsync("/v1/batches", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<dynamic>();
        Assert.NotNull(dto);
    }

    [Fact]
    public async Task AddBatchMember_WithValidData_ReturnsNoContent()
    {
        using var client = factory.CreateClient();
        var token = TestJwtTokenBuilder.GenerateAdminToken();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createRequest = new CreateBatchRequest(Guid.NewGuid(), "Test Batch", null, 50, Guid.NewGuid());
        var createResponse = await client.PostAsJsonAsync("/v1/batches", createRequest);
        createResponse.EnsureSuccessStatusCode();
        var batch = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var batchId = batch.GetProperty("id").GetGuid();

        var memberRequest = new AddBatchMemberRequest("candidate@example.com", "+91-9876543210");
        var memberResponse = await client.PostAsJsonAsync($"/v1/batches/{batchId}/members", memberRequest);

        Assert.Equal(HttpStatusCode.NoContent, memberResponse.StatusCode);
    }

    // The tests below exercise rules that depend on the batch's existing members, so they only
    // hold if the aggregate is reloaded *with* its members on each request.

    [Fact]
    public async Task AddBatchMember_WithDuplicateEmail_ReturnsConflict()
    {
        using var client = AuthorizedClient();
        var batchId = await CreateBatchAsync(client, maxMembers: 50);

        var first = await AddMemberAsync(client, batchId, "dup@example.com");
        var second = await AddMemberAsync(client, batchId, "dup@example.com");

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task AddBatchMember_BeyondCapacity_ReturnsBadRequest()
    {
        using var client = AuthorizedClient();
        var batchId = await CreateBatchAsync(client, maxMembers: 1);

        var first = await AddMemberAsync(client, batchId, "one@example.com");
        var second = await AddMemberAsync(client, batchId, "two@example.com");

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task ActivateBatch_WithMember_ReturnsNoContent()
    {
        using var client = AuthorizedClient();
        var batchId = await CreateBatchAsync(client, maxMembers: 50);
        (await AddMemberAsync(client, batchId, "member@example.com")).EnsureSuccessStatusCode();

        var response = await client.PostAsync($"/v1/batches/{batchId}/activate", content: null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task ActivateBatch_WithoutMembers_ReturnsBadRequest()
    {
        using var client = AuthorizedClient();
        var batchId = await CreateBatchAsync(client, maxMembers: 50);

        var response = await client.PostAsync($"/v1/batches/{batchId}/activate", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private HttpClient AuthorizedClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtTokenBuilder.GenerateAdminToken());
        return client;
    }

    private static async Task<Guid> CreateBatchAsync(HttpClient client, int maxMembers)
    {
        var request = new CreateBatchRequest(Guid.NewGuid(), "Test Batch", null, maxMembers, Guid.NewGuid());
        var response = await client.PostAsJsonAsync("/v1/batches", request);
        response.EnsureSuccessStatusCode();
        var batch = await response.Content.ReadFromJsonAsync<JsonElement>();
        return batch.GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> AddMemberAsync(HttpClient client, Guid batchId, string email) =>
        client.PostAsJsonAsync($"/v1/batches/{batchId}/members", new AddBatchMemberRequest(email, null));

    [Fact]
    public async Task CreateBatch_WithoutAuth_ReturnsUnauthorized()
    {
        using var client = factory.CreateClient();

        var request = new CreateBatchRequest(Guid.NewGuid(), "Test Batch", null, 50, Guid.NewGuid());
        var response = await client.PostAsJsonAsync("/v1/batches", request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
