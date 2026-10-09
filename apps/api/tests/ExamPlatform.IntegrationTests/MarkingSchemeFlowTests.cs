using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>The exam author's marking scheme (FR-12) over real HTTP and a real database.</summary>
public sealed class MarkingSchemeFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await JsonAsync(response)).GetProperty("title").GetString());
    }

    private static async Task<(decimal Correct, decimal Incorrect, decimal Unattempted)> MarksOfAsync(HttpClient admin, Guid examId)
    {
        var scheme = (await admin.GetFromJsonAsync<JsonElement>($"/v1/exams/{examId}")).GetProperty("config").GetProperty("markingScheme");
        return (scheme.GetProperty("correctMarks").GetDecimal(), scheme.GetProperty("incorrectMarks").GetDecimal(), scheme.GetProperty("unattemptedMarks").GetDecimal());
    }

    private static async Task<Guid> DraftExamAsync(HttpClient admin)
    {
        var response = await admin.PostAsJsonAsync("/v1/exams", new { seriesId = (Guid?)null, name = "Marking exam" });
        response.EnsureSuccessStatusCode();
        return (await JsonAsync(response)).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task TheAuthor_CanSetTheMarks_OnADraft_AndGetsTheExamBack()
    {
        using var admin = await factory.AdminClientAsync();
        var examId = await DraftExamAsync(admin);

        var response = await admin.PutAsJsonAsync($"/v1/exams/{examId}/marking-scheme", new { correctMarks = 4, incorrectMarks = -1, unattemptedMarks = 0 });

        response.EnsureSuccessStatusCode();
        Assert.Equal((4m, -1m, 0m), await MarksOfAsync(admin, examId));
    }

    [Fact]
    public async Task ASchemeOutOfRange_IsRefusedWithAReason_AndTheMarksStay()
    {
        using var admin = await factory.AdminClientAsync();
        var examId = await DraftExamAsync(admin);

        await AssertProblemAsync(
            await admin.PutAsJsonAsync($"/v1/exams/{examId}/marking-scheme", new { correctMarks = 0, incorrectMarks = 0, unattemptedMarks = 0 }),
            HttpStatusCode.BadRequest, "invalid_exam_config");

        Assert.Equal((1m, 0m, 0m), await MarksOfAsync(admin, examId));
    }

    [Fact]
    public async Task ABodyMissingAMark_IsRefusedWithAReason_NotACrash()
    {
        using var admin = await factory.AdminClientAsync();
        var examId = await DraftExamAsync(admin);

        await AssertProblemAsync(
            await admin.PutAsJsonAsync($"/v1/exams/{examId}/marking-scheme", new { correctMarks = 4 }), HttpStatusCode.BadRequest, "invalid_exam_config");
    }

    [Fact]
    public async Task APublishedExam_RefusesNewMarks()
    {
        using var admin = await factory.AdminClientAsync();
        var question = await CreateQuestionAsync(admin, "Marks question", "Right", "Wrong");
        var examId = await CreateExamAsync(admin, "Marks exam", [question], TimeSpan.FromMinutes(-5));

        await AssertProblemAsync(
            await admin.PutAsJsonAsync($"/v1/exams/{examId}/marking-scheme", new { correctMarks = 4, incorrectMarks = -1, unattemptedMarks = 0 }),
            HttpStatusCode.Conflict, "exam_not_draft");

        Assert.Equal((1m, 0m, 0m), await MarksOfAsync(admin, examId));
    }

    [Fact]
    public async Task ForAnExamThatDoesNotExist_TheRouteAnswersNotFound()
    {
        using var admin = await factory.AdminClientAsync();

        await AssertProblemAsync(
            await admin.PutAsJsonAsync($"/v1/exams/{Guid.NewGuid()}/marking-scheme", new { correctMarks = 2, incorrectMarks = 0, unattemptedMarks = 0 }),
            HttpStatusCode.NotFound, "exam_not_found");
    }

    [Fact]
    public async Task ACandidate_CannotSetTheMarks()
    {
        using var admin = await factory.AdminClientAsync();
        var question = await CreateQuestionAsync(admin, "Marks question 2", "Right", "Wrong");
        var examId = await CreateExamAsync(admin, "Marks exam 2", [question], TimeSpan.FromMinutes(-5));
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;

        var response = await candidate.PutAsJsonAsync($"/v1/exams/{examId}/marking-scheme", new { correctMarks = 9, incorrectMarks = 0, unattemptedMarks = 0 });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
