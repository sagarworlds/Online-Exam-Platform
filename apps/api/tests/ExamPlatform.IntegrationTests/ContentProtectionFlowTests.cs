using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// The author chooses whether the exam page turns off copying, pasting, right-click and printing (FR-23), and the candidate's
/// attempt carries that choice to the page.
/// </summary>
public sealed class ContentProtectionFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<Guid> PublishedExamAsync(HttpClient admin)
    {
        var question = await CreateQuestionAsync(admin, "What is 2 + 2?", "4", "5");
        return await CreateExamAsync(admin, "Protection Exam", [question], startsIn: TimeSpan.FromMinutes(-5));
    }

    private static async Task<JsonElement> ExamAsync(HttpClient admin, Guid examId) =>
        await admin.GetFromJsonAsync<JsonElement>($"/v1/exams/{examId}");

    private static async Task<JsonElement> StartAsync(HttpClient candidate, Guid examId)
    {
        var response = await candidate.PostAsJsonAsync($"/v1/me/exams/{examId}/attempts", new { instructionsAcknowledged = true });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task ANewExam_IsProtected()
    {
        using var admin = await factory.AdminClientAsync();
        var examId = await PublishedExamAsync(admin);

        Assert.True((await ExamAsync(admin, examId)).GetProperty("config").GetProperty("contentProtection").GetBoolean());
    }

    [Fact]
    public async Task TheAuthor_CanLiftItAndRestoreIt_EvenAfterPublishing()
    {
        using var admin = await factory.AdminClientAsync();
        var examId = await PublishedExamAsync(admin);

        var lifted = await admin.PutAsJsonAsync($"/v1/exams/{examId}/content-protection", new { contentProtection = false });

        Assert.Equal(HttpStatusCode.OK, lifted.StatusCode);
        Assert.False((await lifted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("config").GetProperty("contentProtection").GetBoolean());
        Assert.False((await ExamAsync(admin, examId)).GetProperty("config").GetProperty("contentProtection").GetBoolean());

        await admin.PutAsJsonAsync($"/v1/exams/{examId}/content-protection", new { contentProtection = true });
        Assert.True((await ExamAsync(admin, examId)).GetProperty("config").GetProperty("contentProtection").GetBoolean());
    }

    [Fact]
    public async Task AMissingChoice_IsA400_NotASilentOff()
    {
        using var admin = await factory.AdminClientAsync();
        var examId = await PublishedExamAsync(admin);

        var response = await admin.PutAsJsonAsync($"/v1/exams/{examId}/content-protection", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_exam_config", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
        Assert.True((await ExamAsync(admin, examId)).GetProperty("config").GetProperty("contentProtection").GetBoolean());
    }

    [Fact]
    public async Task AnUnknownExam_Is404()
    {
        using var admin = await factory.AdminClientAsync();

        var response = await admin.PutAsJsonAsync($"/v1/exams/{Guid.NewGuid()}/content-protection", new { contentProtection = false });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task TheCandidatesAttempt_CarriesTheChoice_AndAnOpenAttemptSeesALaterChange()
    {
        using var admin = await factory.AdminClientAsync();
        var examId = await PublishedExamAsync(admin);
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;

        var attempt = await StartAsync(candidate, examId);
        var attemptId = attempt.GetProperty("id").GetGuid();
        Assert.True(attempt.GetProperty("contentProtection").GetBoolean());

        (await admin.PutAsJsonAsync($"/v1/exams/{examId}/content-protection", new { contentProtection = false })).EnsureSuccessStatusCode();

        var reloaded = await candidate.GetFromJsonAsync<JsonElement>($"/v1/me/attempts/{attemptId}");
        Assert.False(reloaded.GetProperty("contentProtection").GetBoolean());
    }
}
