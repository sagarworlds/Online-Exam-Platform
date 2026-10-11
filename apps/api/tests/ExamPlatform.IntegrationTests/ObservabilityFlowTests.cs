using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExamPlatform.Modules.Admin.Infrastructure;
using ExamPlatform.SharedKernel.Infrastructure.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// The correlation id and the health routes against the real pipeline (NFR-9, NFR-3). The one id a caller sees in the response
/// header is the id in the problem details and in the audit trail.
/// </summary>
public sealed class ObservabilityFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Header = CorrelationId.HeaderName;

    [Fact]
    public async Task LivenessAndReadiness_AnswerHealthy_WithADatabase()
    {
        using var client = factory.CreateClient();

        var live = await client.GetAsync("/v1/health/live");
        var ready = await client.GetAsync("/v1/health/ready");

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal("Healthy", await ready.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ACallersId_IsEchoed_OnASuccessfulResponse()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/health");
        request.Headers.Add(Header, "caller-trace-42");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("caller-trace-42", Assert.Single(response.Headers.GetValues(Header)));
    }

    [Fact]
    public async Task ACallersId_IsEchoed_OnAnError()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/no-such-route");
        request.Headers.Add(Header, "caller-trace-43");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("caller-trace-43", Assert.Single(response.Headers.GetValues(Header)));
    }

    [Fact]
    public async Task WithoutACallersId_ANewOneIsReturned()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/v1/health");

        Assert.Matches("^[0-9a-f]{32}$", Assert.Single(response.Headers.GetValues(Header)));
    }

    [Fact]
    public async Task AnUnacceptableCallersId_IsReplaced_NotRejected()
    {
        using var client = factory.CreateClient();
        var tooLong = new string('x', CorrelationId.MaxLength + 72);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/health");
        request.Headers.Add(Header, tooLong);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var returned = Assert.Single(response.Headers.GetValues(Header));
        Assert.NotEqual(tooLong, returned);
        Assert.Matches("^[0-9a-f]{32}$", returned);
    }

    [Fact]
    public async Task TheProblemTraceId_IsTheResponseHeaderId()
    {
        using var admin = await factory.AdminClientAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/v1/batches/{Guid.NewGuid()}/activate");
        request.Headers.Add(Header, "problem-trace-7");

        var response = await admin.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("problem-trace-7", problem.GetProperty("traceId").GetString());
        Assert.Equal("problem-trace-7", Assert.Single(response.Headers.GetValues(Header)));
    }

    [Fact]
    public async Task TheAuditTrail_RecordsTheRequestsId()
    {
        using var admin = await factory.AdminClientAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/exams")
        {
            Content = JsonContent.Create(new { name = "Correlated exam" }),
        };
        request.Headers.Add(Header, "audit-trace-9");

        var response = await admin.SendAsync(request);

        response.EnsureSuccessStatusCode();
        var examId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var scope = factory.Services.CreateScope();
        var entry = await scope.ServiceProvider.GetRequiredService<AdminDbContext>().AuditLogs
            .AsNoTracking()
            .SingleAsync(a => a.EntityId == examId.ToString() && a.Action == "ExamAuthoring.ExamCreated");
        Assert.Equal("audit-trace-9", entry.CorrelationId);
    }
}

/// <summary>
/// The liveness and readiness split (NFR-3) on a host with no reachable database: the process is alive, so liveness answers,
/// but it is not ready, and the original route Render checks still answers. Runs without a database container.
/// </summary>
public sealed class HealthProbeSplitTests
{
    [Fact]
    public async Task WithoutTheDatabase_LivenessAnswers_ButReadinessReportsUnavailable()
    {
        using var factory = new ProductionHostFactory(otpProvider: null, allowCapturingSender: true);
        using var client = factory.CreateClient();

        var live = await client.GetAsync("/v1/health/live");
        var ready = await client.GetAsync("/v1/health/ready");

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
    }

    [Fact]
    public async Task WithoutTheDatabase_TheOriginalHealthRouteStillAnswersHealthy()
    {
        using var factory = new ProductionHostFactory(otpProvider: null, allowCapturingSender: true);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/v1/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }
}
