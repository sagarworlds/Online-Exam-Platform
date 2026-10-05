using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>What a failed request looks like to a caller, and the audit log's paging limits (FR-40, NFR-5, section 11).</summary>
public sealed class ProblemResponseTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task ADomainError_IsAProblemJsonDocument_WithATraceId()
    {
        using var admin = await factory.AdminClientAsync();

        var response = await admin.PostAsync($"/v1/batches/{Guid.NewGuid()}/activate", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("batch_not_found", problem.GetProperty("title").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task TheAuditLog_ServesAPage_WithinTheLimits()
    {
        using var admin = await factory.AdminClientAsync();

        var response = await admin.GetAsync("/v1/admin/audit-logs?page=1&pageSize=5");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<JsonElement>()).GetArrayLength() <= 5);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("page=-3")]
    [InlineData("page=2000000000")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=-1")]
    [InlineData("pageSize=201")]
    public async Task TheAuditLog_RefusesAPageOutsideTheLimits_With400(string query)
    {
        using var admin = await factory.AdminClientAsync();

        var response = await admin.GetAsync($"/v1/admin/audit-logs?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_page_request", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
    }
}
