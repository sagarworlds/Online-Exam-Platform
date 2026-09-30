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

    [Fact]
    public async Task CreateBatch_WithoutAuth_ReturnsUnauthorized()
    {
        using var client = factory.CreateClient();

        var request = new CreateBatchRequest(Guid.NewGuid(), "Test Batch", null, 50, Guid.NewGuid());
        var response = await client.PostAsJsonAsync("/v1/batches", request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
