using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Net.Http.Headers;
using ExamPlatform.Modules.Guardian.Endpoints;

namespace ExamPlatform.IntegrationTests;

public class GuardianFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task CreateGuardian_WithValidData_ReturnsCreatedResponse()
    {
        using var client = factory.CreateClient();
        // Creating and linking guardians is a staff action (guardian.link.manage), not something a guardian account may do (FR-2).
        var token = (await factory.SignInAsAsync("ExamAdmin")).AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var request = new CreateGuardianRequest(
            "guardian@example.com",
            "John Guardian",
            "+91-9876543210");

        var response = await client.PostAsJsonAsync("/v1/guardians", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<dynamic>();
        Assert.NotNull(dto);
    }

    [Fact]
    public async Task LinkCandidate_WithValidData_ReturnsCreatedResponse()
    {
        using var client = factory.CreateClient();
        // Creating and linking guardians is a staff action (guardian.link.manage), not something a guardian account may do (FR-2).
        var token = (await factory.SignInAsAsync("ExamAdmin")).AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var guardianRequest = new CreateGuardianRequest("guardian@example.com", "John Guardian", null);
        var guardianResponse = await client.PostAsJsonAsync("/v1/guardians", guardianRequest);
        guardianResponse.EnsureSuccessStatusCode();
        var guardian = await guardianResponse.Content.ReadFromJsonAsync<JsonElement>();
        var guardianId = guardian.GetProperty("id").GetGuid();

        var linkRequest = new LinkCandidateRequest(Guid.NewGuid(), "candidate@example.com");
        var linkResponse = await client.PostAsJsonAsync($"/v1/guardians/{guardianId}/links", linkRequest);

        Assert.Equal(HttpStatusCode.Created, linkResponse.StatusCode);
    }

    [Fact]
    public async Task LinkCandidate_SameCandidateTwice_Returns409()
    {
        using var client = await StaffClientAsync();
        var guardianId = await CreateGuardianAsync(client);
        var candidateId = Guid.NewGuid();
        (await LinkAsync(client, guardianId, candidateId)).EnsureSuccessStatusCode();

        // A separate request: the first link has to be loaded back, or the duplicate is stored.
        var response = await LinkAsync(client, guardianId, candidateId);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "guardian_already_linked");
    }

    [Fact]
    public async Task RevokeLink_ThenRevokeAgain_Returns409()
    {
        using var client = await StaffClientAsync();
        var guardianId = await CreateGuardianAsync(client);
        var candidateId = Guid.NewGuid();
        (await LinkAsync(client, guardianId, candidateId)).EnsureSuccessStatusCode();

        var first = await client.DeleteAsync($"/v1/guardians/{guardianId}/links/{candidateId}");
        var second = await client.DeleteAsync($"/v1/guardians/{guardianId}/links/{candidateId}");

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        await AssertProblemAsync(second, HttpStatusCode.Conflict, "guardian_link_already_revoked");
    }

    [Fact]
    public async Task RevokeLink_ForACandidateNeverLinked_Returns404_NotASilent204()
    {
        using var client = await StaffClientAsync();
        var guardianId = await CreateGuardianAsync(client);

        var response = await client.DeleteAsync($"/v1/guardians/{guardianId}/links/{Guid.NewGuid()}");

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "guardian_link_not_found");
    }

    [Fact]
    public async Task UnlinkCandidate_ThenLinkAgain_Succeeds()
    {
        using var client = await StaffClientAsync();
        var guardianId = await CreateGuardianAsync(client);
        var candidateId = Guid.NewGuid();
        (await LinkAsync(client, guardianId, candidateId)).EnsureSuccessStatusCode();

        var unlink = await client.DeleteAsync($"/v1/guardians/{guardianId}/candidates/{candidateId}");
        var relink = await LinkAsync(client, guardianId, candidateId);

        Assert.Equal(HttpStatusCode.NoContent, unlink.StatusCode);
        Assert.Equal(HttpStatusCode.Created, relink.StatusCode);
    }

    [Fact]
    public async Task GuardianRoutes_UnknownGuardian_Return404WithTitleGuardianNotFound()
    {
        using var client = await StaffClientAsync();
        var unknown = Guid.NewGuid();

        await AssertProblemAsync(await LinkAsync(client, unknown, Guid.NewGuid()), HttpStatusCode.NotFound, "guardian_not_found");
        await AssertProblemAsync(await client.DeleteAsync($"/v1/guardians/{unknown}/links/{Guid.NewGuid()}"), HttpStatusCode.NotFound, "guardian_not_found");
        await AssertProblemAsync(await client.DeleteAsync($"/v1/guardians/{unknown}/candidates/{Guid.NewGuid()}"), HttpStatusCode.NotFound, "guardian_not_found");
    }

    [Theory]
    [InlineData("not-an-email", "Gia Guardian")]
    [InlineData("guardian@example.com", "   ")]
    public async Task CreateGuardian_WithInvalidDetails_Returns400WithTitleInvalidGuardian(string email, string fullName)
    {
        using var client = await StaffClientAsync();

        var response = await client.PostAsJsonAsync("/v1/guardians", new CreateGuardianRequest(email, fullName, null));

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "invalid_guardian");
    }

    private async Task<HttpClient> StaffClientAsync()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await factory.SignInAsAsync("ExamAdmin")).AccessToken);
        return client;
    }

    private static async Task<Guid> CreateGuardianAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/v1/guardians", new CreateGuardianRequest("guardian@example.com", "Gia Guardian", null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> LinkAsync(HttpClient client, Guid guardianId, Guid candidateId) =>
        client.PostAsJsonAsync($"/v1/guardians/{guardianId}/links", new LinkCandidateRequest(candidateId, "candidate@example.com"));

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string errorCode)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(errorCode, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
    }

    [Fact]
    public async Task CreateGuardian_WithoutAuth_ReturnsUnauthorized()
    {
        using var client = factory.CreateClient();

        var request = new CreateGuardianRequest("guardian@example.com", "John Guardian", null);
        var response = await client.PostAsJsonAsync("/v1/guardians", request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
