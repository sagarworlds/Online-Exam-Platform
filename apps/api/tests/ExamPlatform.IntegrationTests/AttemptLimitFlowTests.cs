using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// The exam author's choice of how many attempts every enrolled candidate has (FR-12), over real HTTP and a real database:
/// the setting itself, its refusals and who may use it, and what the runtime then allows a candidate to do.
/// </summary>
public sealed class AttemptLimitFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await JsonAsync(response)).GetProperty("title").GetString());
    }

    private static async Task<Guid> PublishedExamAsync(HttpClient admin)
    {
        var question = await CreateQuestionAsync(admin, "Limit question", "Right", "Wrong");
        return await CreateExamAsync(admin, "Limit exam", [question], TimeSpan.FromMinutes(-5));
    }

    private static async Task<int> LimitOfAsync(HttpClient admin, Guid examId) =>
        (await admin.GetFromJsonAsync<JsonElement>($"/v1/exams/{examId}")).GetProperty("config").GetProperty("maxAttempts").GetInt32();

    // ---- the setting ------------------------------------------------------------------------------

    [Fact]
    public async Task AnExam_AllowsOneAttempt_UnlessTheAuthorSaysOtherwise()
    {
        using var admin = await factory.AdminClientAsync();
        var examId = await PublishedExamAsync(admin);

        Assert.Equal(1, await LimitOfAsync(admin, examId));
    }

    [Fact]
    public async Task TheAuthor_CanSetTheLimit_EvenOnceTheExamIsPublished_AndGetsTheExamBack()
    {
        using var admin = await factory.AdminClientAsync();
        var examId = await PublishedExamAsync(admin);

        var response = await admin.PutAsJsonAsync($"/v1/exams/{examId}/attempt-limit", new { maxAttempts = 3 });

        response.EnsureSuccessStatusCode();
        Assert.Equal(3, (await JsonAsync(response)).GetProperty("config").GetProperty("maxAttempts").GetInt32());
        Assert.Equal(3, await LimitOfAsync(admin, examId));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    [InlineData(11)]
    public async Task ANumberOutsideOneToTen_IsRefusedWithAReason_AndTheLimitStays(int attempts)
    {
        using var admin = await factory.AdminClientAsync();
        var examId = await PublishedExamAsync(admin);

        await AssertProblemAsync(
            await admin.PutAsJsonAsync($"/v1/exams/{examId}/attempt-limit", new { maxAttempts = attempts }), HttpStatusCode.BadRequest, "invalid_exam_config");

        Assert.Equal(1, await LimitOfAsync(admin, examId));
    }

    [Fact]
    public async Task ABodyWithNoNumber_IsRefusedWithAReason_NotACrash()
    {
        using var admin = await factory.AdminClientAsync();
        var examId = await PublishedExamAsync(admin);

        await AssertProblemAsync(await admin.PutAsJsonAsync($"/v1/exams/{examId}/attempt-limit", new { }), HttpStatusCode.BadRequest, "invalid_exam_config");
    }

    [Fact]
    public async Task ForAnExamThatDoesNotExist_TheRouteAnswersNotFound()
    {
        using var admin = await factory.AdminClientAsync();

        await AssertProblemAsync(
            await admin.PutAsJsonAsync($"/v1/exams/{Guid.NewGuid()}/attempt-limit", new { maxAttempts = 2 }), HttpStatusCode.NotFound, "exam_not_found");
    }

    [Fact]
    public async Task ACandidate_CannotSetTheLimit()
    {
        using var admin = await factory.AdminClientAsync();
        var examId = await PublishedExamAsync(admin);
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;

        var response = await candidate.PutAsJsonAsync($"/v1/exams/{examId}/attempt-limit", new { maxAttempts = 5 });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(1, await LimitOfAsync(admin, examId));
    }
}
