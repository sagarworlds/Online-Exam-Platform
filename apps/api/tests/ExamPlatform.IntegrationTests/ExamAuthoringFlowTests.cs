using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using ExamPlatform.Modules.ExamAuthoring.Endpoints;

namespace ExamPlatform.IntegrationTests;

public class ExamAuthoringFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task CreateExam_WithValidData_ReturnsCreatedResponse()
    {
        using var client = factory.CreateClient();
        var token = TestJwtTokenBuilder.GenerateAdminToken();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var seriesId = Guid.NewGuid();
        var createdBy = Guid.NewGuid();
        var request = new CreateExamRequest(
            seriesId,
            "Integration Test Exam",
            "Full system test",
            createdBy);

        var response = await client.PostAsJsonAsync("/v1/exams", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<dynamic>();
        Assert.NotNull(dto);
    }

    [Fact]
    public async Task CreateExam_WithoutAuth_ReturnsUnauthorized()
    {
        using var client = factory.CreateClient();

        var request = new CreateExamRequest(
            Guid.NewGuid(),
            "Test Exam",
            null,
            Guid.NewGuid());

        var response = await client.PostAsJsonAsync("/v1/exams", request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
