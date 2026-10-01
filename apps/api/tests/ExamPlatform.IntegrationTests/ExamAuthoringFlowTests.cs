using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ExamPlatform.Modules.ExamAuthoring.Endpoints;

namespace ExamPlatform.IntegrationTests;

public class ExamAuthoringFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task CreateExam_WithValidData_ReturnsCreatedResponse()
    {
        using var client = AuthorizedClient(await factory.SignInAsAsync("SuperAdmin"));

        var request = new CreateExamRequest(
            Guid.NewGuid(),
            "Integration Test Exam",
            "Full system test");

        var response = await client.PostAsJsonAsync("/v1/exams", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<dynamic>();
        Assert.NotNull(dto);
    }

    [Fact]
    public async Task CreateExam_WithSpoofedCreatedByInBody_AttributesCreatorFromTokenSubject()
    {
        var caller = await factory.SignInAsAsync("SuperAdmin");
        using var client = AuthorizedClient(caller);

        // An older client (or an attacker) may still send createdBy; it must be ignored.
        var spoofedCreator = Guid.NewGuid();
        var response = await client.PostAsJsonAsync("/v1/exams", new
        {
            seriesId = Guid.NewGuid(),
            name = "Spoofed Creator Exam",
            description = (string?)null,
            createdBy = spoofedCreator,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var exam = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(caller.UserId, exam.GetProperty("createdBy").GetGuid());
        Assert.NotEqual(spoofedCreator, exam.GetProperty("createdBy").GetGuid());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreateExam_WithoutSeries_CreatesAStandaloneExam(bool sendExplicitNull)
    {
        using var client = AuthorizedClient(await factory.SignInAsAsync("SuperAdmin"));

        // The exam builder sends null for a blank series field; a client may also omit it.
        var response = sendExplicitNull
            ? await client.PostAsJsonAsync("/v1/exams", new { seriesId = (Guid?)null, name = "Standalone Exam" })
            : await client.PostAsJsonAsync("/v1/exams", new { name = "Standalone Exam" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var exam = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, exam.GetProperty("seriesId").ValueKind);
    }

    [Fact]
    public async Task CreateExam_WithEmptySeriesGuid_Returns400()
    {
        using var client = AuthorizedClient(await factory.SignInAsAsync("SuperAdmin"));

        var response = await client.PostAsJsonAsync("/v1/exams", new { seriesId = Guid.Empty, name = "Empty Series Exam" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_exam_config", problem.GetProperty("title").GetString());
    }

    [Fact]
    public async Task CreateExam_WithoutAuth_ReturnsUnauthorized()
    {
        using var client = factory.CreateClient();

        var request = new CreateExamRequest(Guid.NewGuid(), "Test Exam", null);

        var response = await client.PostAsJsonAsync("/v1/exams", request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private HttpClient AuthorizedClient(SignedInTestUser caller)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", caller.AccessToken);
        return client;
    }
}
