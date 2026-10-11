using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace ExamPlatform.IntegrationTests;

/// <summary>Data-principal requests (FR-48) over HTTP: a candidate asks, a super-admin answers, and the candidate sees the answer.</summary>
public class DataRequestFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task ACandidateAsks_AStaffAnswer_AndTheCandidateSeesIt()
    {
        using var candidate = await ClientAsync("Candidate");
        var raised = await candidate.PostAsJsonAsync("/v1/me/data-requests", new { kind = "Access", details = "Send me a copy." });
        raised.EnsureSuccessStatusCode();
        var id = (await raised.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var staff = await ClientAsync("SuperAdmin");
        var open = await staff.GetFromJsonAsync<JsonElement>("/v1/data-requests/open");
        Assert.Contains(open.EnumerateArray(), request => request.GetProperty("id").GetGuid() == id);

        var answered = await staff.PostAsJsonAsync($"/v1/data-requests/{id}/resolve", new { outcome = "Completed", note = "Copy sent." });
        Assert.Equal(HttpStatusCode.OK, answered.StatusCode);

        var mine = await candidate.GetFromJsonAsync<JsonElement>("/v1/me/data-requests");
        var seen = mine.EnumerateArray().Single(request => request.GetProperty("id").GetGuid() == id);
        Assert.Equal("Completed", seen.GetProperty("status").GetString());
        Assert.Equal("Copy sent.", seen.GetProperty("resolutionNote").GetString());
    }

    [Fact]
    public async Task ASecondOpenRequestOfTheSameKind_IsRefused_UntilTheFirstIsAnswered()
    {
        using var candidate = await ClientAsync("Candidate");
        var first = await candidate.PostAsJsonAsync("/v1/me/data-requests", new { kind = "Erasure", details = (string?)null });
        first.EnsureSuccessStatusCode();

        var second = await candidate.PostAsJsonAsync("/v1/me/data-requests", new { kind = "Erasure", details = (string?)null });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("data_request_already_open", (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
    }

    [Fact]
    public async Task ARefusal_WithoutAReason_IsRefused()
    {
        using var candidate = await ClientAsync("Candidate");
        var raised = await candidate.PostAsJsonAsync("/v1/me/data-requests", new { kind = "Correction", details = "Wrong date." });
        var id = (await raised.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var staff = await ClientAsync("SuperAdmin");
        var refused = await staff.PostAsJsonAsync($"/v1/data-requests/{id}/resolve", new { outcome = "Rejected", note = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("invalid_data_request", (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
    }

    [Fact]
    public async Task AnUnknownRequest_IsNotFound()
    {
        using var staff = await ClientAsync("SuperAdmin");

        var response = await staff.PostAsJsonAsync($"/v1/data-requests/{Guid.NewGuid()}/resolve", new { outcome = "Completed", note = (string?)null });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<HttpClient> ClientAsync(string role)
    {
        var client = factory.CreateClient();
        var token = (await factory.SignInAsAsync(role)).AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
