using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// The exam author's choice of when candidates may see which answers were right, over real HTTP and a real database:
/// the three modes, the manual release, and the refusals.
/// </summary>
public sealed class ResultReleaseFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await JsonAsync(response)).GetProperty("title").GetString());
    }

    private static async Task<Guid> PublishedExamAsync(HttpClient admin)
    {
        var question = await CreateQuestionAsync(admin, "Release question", "Right", "Wrong");
        return await CreateExamAsync(admin, "Release exam", [question], TimeSpan.FromHours(1));
    }

    private static async Task<JsonElement> ConfigOfAsync(HttpClient admin, Guid examId) =>
        (await admin.GetFromJsonAsync<JsonElement>($"/v1/exams/{examId}")).GetProperty("config");

    [Fact]
    public async Task AnExam_ShowsAnswersRightAfterSubmitting_UnlessTheAuthorSaysOtherwise()
    {
        using var admin = await factory.AdminClientAsync();
        var examId = await PublishedExamAsync(admin);

        var config = await ConfigOfAsync(admin, examId);

        Assert.Equal("Instant", config.GetProperty("resultReleaseMode").GetString());
        Assert.Equal(JsonValueKind.Null, config.GetProperty("resultReleaseTime").ValueKind);
    }

    [Fact]
    public async Task TheAuthor_CanSchedule_TheAnswersForALaterTime_EvenOnceTheExamIsPublished()
    {
        using var admin = await factory.AdminClientAsync();
        var examId = await PublishedExamAsync(admin);
        var at = DateTime.UtcNow.AddDays(3);

        var response = await admin.PutAsJsonAsync($"/v1/exams/{examId}/result-release", new { mode = "Scheduled", releaseTime = at });

        response.EnsureSuccessStatusCode();
        var config = await ConfigOfAsync(admin, examId);
        Assert.Equal("Scheduled", config.GetProperty("resultReleaseMode").GetString());
        Assert.Equal(at, config.GetProperty("resultReleaseTime").GetDateTime(), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task AScheduledRelease_WithoutATime_IsRefusedWithAReason()
    {
        using var admin = await factory.AdminClientAsync();
        var examId = await PublishedExamAsync(admin);

        await AssertProblemAsync(
            await admin.PutAsJsonAsync($"/v1/exams/{examId}/result-release", new { mode = "Scheduled" }),
            HttpStatusCode.BadRequest, "invalid_exam_config");
        Assert.Equal("Instant", (await ConfigOfAsync(admin, examId)).GetProperty("resultReleaseMode").GetString());
    }

    [Fact]
    public async Task ABodyWithNoMode_IsRefusedWithAReason_NotACrash()
    {
        using var admin = await factory.AdminClientAsync();
        var examId = await PublishedExamAsync(admin);

        await AssertProblemAsync(await admin.PutAsJsonAsync($"/v1/exams/{examId}/result-release", new { }), HttpStatusCode.BadRequest, "invalid_exam_config");
    }

    [Fact]
    public async Task AModeThatDoesNotExist_IsRefused()
    {
        using var admin = await factory.AdminClientAsync();
        var examId = await PublishedExamAsync(admin);

        var response = await admin.PutAsJsonAsync($"/v1/exams/{examId}/result-release", new { mode = "Whenever" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AManualExam_IsReleasedByHand_AndReleasingAgainKeepsTheFirstTime()
    {
        using var admin = await factory.AdminClientAsync();
        var examId = await PublishedExamAsync(admin);
        (await admin.PutAsJsonAsync($"/v1/exams/{examId}/result-release", new { mode = "Manual" })).EnsureSuccessStatusCode();
        Assert.Equal(JsonValueKind.Null, (await ConfigOfAsync(admin, examId)).GetProperty("resultReleaseTime").ValueKind);

        var released = await JsonAsync((await admin.PostAsync($"/v1/exams/{examId}/results/release", content: null)).EnsureSuccessStatusCode());
        var first = released.GetProperty("config").GetProperty("resultReleaseTime").GetDateTime();
        var again = await JsonAsync((await admin.PostAsync($"/v1/exams/{examId}/results/release", content: null)).EnsureSuccessStatusCode());

        // The first answer carries the in-memory instant, the second the stored one; Postgres keeps microseconds, .NET ticks are finer.
        Assert.Equal(first, again.GetProperty("config").GetProperty("resultReleaseTime").GetDateTime(), TimeSpan.FromMilliseconds(1));
        Assert.True(Math.Abs((first - DateTime.UtcNow).TotalMinutes) < 5);
    }

    [Fact]
    public async Task AnExam_NotSetToManualRelease_CannotBeReleasedByHand()
    {
        using var admin = await factory.AdminClientAsync();
        var examId = await PublishedExamAsync(admin);

        await AssertProblemAsync(await admin.PostAsync($"/v1/exams/{examId}/results/release", content: null), HttpStatusCode.BadRequest, "invalid_exam_config");
    }

    [Fact]
    public async Task ADraftExam_HasNoAnswersToRelease()
    {
        using var admin = await factory.AdminClientAsync();
        var draft = await JsonAsync((await admin.PostAsJsonAsync("/v1/exams", new { name = "Draft" })).EnsureSuccessStatusCode());
        var examId = draft.GetProperty("id").GetGuid();
        (await admin.PutAsJsonAsync($"/v1/exams/{examId}/result-release", new { mode = "Manual" })).EnsureSuccessStatusCode();

        await AssertProblemAsync(await admin.PostAsync($"/v1/exams/{examId}/results/release", content: null), HttpStatusCode.BadRequest, "invalid_exam_config");
    }

    [Fact]
    public async Task ForAnExamThatDoesNotExist_BothRoutesAnswerNotFound()
    {
        using var admin = await factory.AdminClientAsync();

        await AssertProblemAsync(await admin.PutAsJsonAsync($"/v1/exams/{Guid.NewGuid()}/result-release", new { mode = "Instant" }), HttpStatusCode.NotFound, "exam_not_found");
        await AssertProblemAsync(await admin.PostAsync($"/v1/exams/{Guid.NewGuid()}/results/release", content: null), HttpStatusCode.NotFound, "exam_not_found");
    }
}
