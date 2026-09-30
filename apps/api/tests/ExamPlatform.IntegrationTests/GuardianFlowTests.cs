using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using ExamPlatform.Modules.Guardian.Endpoints;

namespace ExamPlatform.IntegrationTests;

public class GuardianFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task CreateGuardian_WithValidData_ReturnsCreatedResponse()
    {
        using var client = factory.CreateClient();
        var token = TestJwtTokenBuilder.GenerateGuardianToken();
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
        var token = TestJwtTokenBuilder.GenerateGuardianToken();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var guardianRequest = new CreateGuardianRequest("guardian@example.com", "John Guardian", null);
        var guardianResponse = await client.PostAsJsonAsync("/v1/guardians", guardianRequest);
        guardianResponse.EnsureSuccessStatusCode();
        var guardian = await guardianResponse.Content.ReadFromJsonAsync<dynamic>();
        var guardianId = guardian!.id;

        var linkRequest = new LinkCandidateRequest(Guid.NewGuid(), "candidate@example.com");
        var linkResponse = await client.PostAsJsonAsync($"/v1/guardians/{guardianId}/links", linkRequest);

        Assert.Equal(HttpStatusCode.Created, linkResponse.StatusCode);
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
