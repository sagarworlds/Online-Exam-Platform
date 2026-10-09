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

    [Fact]
    public async Task ActivateBatch_AfterMemberAddedInEarlierRequest_Returns204()
    {
        // Each call below is its own request, so the batch is read back from the database every time: the
        // member added first has to be loaded, or the batch looks empty and refuses to activate.
        using var client = AuthorizedClient(await factory.SignInAsAsync("SuperAdmin"));
        var batchId = await CreateBatchAsync(client, maxMembers: 5);
        (await AddMemberAsync(client, batchId, "member@example.com")).EnsureSuccessStatusCode();

        var response = await client.PostAsync($"/v1/batches/{batchId}/activate", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task CloseBatch_AfterActivate_Returns204()
    {
        using var client = AuthorizedClient(await factory.SignInAsAsync("SuperAdmin"));
        var batchId = await CreateBatchAsync(client, maxMembers: 5);
        (await AddMemberAsync(client, batchId, "member@example.com")).EnsureSuccessStatusCode();
        (await client.PostAsync($"/v1/batches/{batchId}/activate", null)).EnsureSuccessStatusCode();

        var response = await client.PostAsync($"/v1/batches/{batchId}/close", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task ActivateBatch_WithoutMembers_Returns400()
    {
        using var client = AuthorizedClient(await factory.SignInAsAsync("SuperAdmin"));
        var batchId = await CreateBatchAsync(client, maxMembers: 5);

        var response = await client.PostAsync($"/v1/batches/{batchId}/activate", null);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "invalid_batch_config");
    }

    [Theory]
    [InlineData("activate")]
    [InlineData("close")]
    public async Task BatchLifecycle_UnknownBatch_Returns404WithTitleBatchNotFound(string action)
    {
        using var client = AuthorizedClient(await factory.SignInAsAsync("SuperAdmin"));

        var response = await client.PostAsync($"/v1/batches/{Guid.NewGuid()}/{action}", null);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "batch_not_found");
    }

    [Fact]
    public async Task AddMember_UnknownBatch_Returns404WithTitleBatchNotFound()
    {
        using var client = AuthorizedClient(await factory.SignInAsAsync("SuperAdmin"));

        var response = await AddMemberAsync(client, Guid.NewGuid(), "member@example.com");

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "batch_not_found");
    }

    [Fact]
    public async Task AddMember_SameEmailInSeparateRequests_Returns409_EvenInAnotherCase()
    {
        using var client = AuthorizedClient(await factory.SignInAsAsync("SuperAdmin"));
        var batchId = await CreateBatchAsync(client, maxMembers: 5);
        (await AddMemberAsync(client, batchId, "member@example.com")).EnsureSuccessStatusCode();

        var response = await AddMemberAsync(client, batchId, "  Member@Example.COM ");

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "duplicate_member");
        Assert.DoesNotContain("member@example.com", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AddMember_BeyondCapacityInSeparateRequests_Returns400()
    {
        using var client = AuthorizedClient(await factory.SignInAsAsync("SuperAdmin"));
        var batchId = await CreateBatchAsync(client, maxMembers: 1);
        (await AddMemberAsync(client, batchId, "first@example.com")).EnsureSuccessStatusCode();

        var response = await AddMemberAsync(client, batchId, "second@example.com");

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "invalid_batch_config");
    }

    [Fact]
    public async Task AddMember_SameEmailConcurrently_OneSucceedsAndTheOtherGets409()
    {
        using var client = AuthorizedClient(await factory.SignInAsAsync("SuperAdmin"));
        var batchId = await CreateBatchAsync(client, maxMembers: 5);

        var responses = await Task.WhenAll(
            AddMemberAsync(client, batchId, "racer@example.com"),
            AddMemberAsync(client, batchId, "racer@example.com"));

        // Both requests can pass the aggregate's own duplicate check; the unique index decides, and the
        // loser must still answer with the typed 409 rather than a 500.
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.NoContent));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
    }

    [Theory]
    [InlineData("not-an-email", null)]
    [InlineData("member@example.com", "+91-98765432101234567890")]
    [InlineData("member@example.com", "call me")]
    public async Task AddMember_WithUnusableContact_Returns400WithTitleInvalidBatchMember(string email, string? phone)
    {
        using var client = AuthorizedClient(await factory.SignInAsAsync("SuperAdmin"));
        var batchId = await CreateBatchAsync(client, maxMembers: 5);

        var response = await AddMemberAsync(client, batchId, email, phone);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "invalid_batch_member");
    }

    [Fact]
    public async Task CreateBatch_WithZeroMaxMembers_Returns400()
    {
        using var client = AuthorizedClient(await factory.SignInAsAsync("SuperAdmin"));

        var response = await client.PostAsJsonAsync("/v1/batches", new CreateBatchRequest(Guid.NewGuid(), "Batch", null, 0));

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "invalid_batch_config");
    }

    private static async Task<Guid> CreateBatchAsync(HttpClient client, int maxMembers)
    {
        var response = await client.PostAsJsonAsync("/v1/batches", new CreateBatchRequest(Guid.NewGuid(), "Batch", null, maxMembers));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> AddMemberAsync(HttpClient client, Guid batchId, string email, string? phone = null) =>
        client.PostAsJsonAsync($"/v1/batches/{batchId}/members", new AddBatchMemberRequest(email, phone));

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string errorCode)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(errorCode, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
    }

    private HttpClient AuthorizedClient(SignedInTestUser caller)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", caller.AccessToken);
        return client;
    }
}
