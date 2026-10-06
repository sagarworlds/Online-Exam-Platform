using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>Staff preview an exam as a candidate would see it (FR-15): in any state, with no answer key, and nothing stored.</summary>
public sealed class ExamPreviewFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnExamInAnyState_IsShownAsACandidateWouldSeeIt_WithoutTheAnswerKey(bool published)
    {
        using var admin = await factory.AdminClientAsync();
        var question = await CreateQuestionAsync(admin, "What is 2 + 2?", "4", "5");
        var examId = await CreateExamAsync(admin, "Preview Exam", [question], startsIn: TimeSpan.FromHours(2), publish: published);

        var response = await admin.GetAsync($"/v1/exams/{examId}/preview");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preview = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Preview Exam", preview.GetProperty("examName").GetString());
        Assert.Equal("InProgress", preview.GetProperty("status").GetString());
        var shown = Assert.Single(preview.GetProperty("sections")[0].GetProperty("questions").EnumerateArray());
        Assert.Equal("<p>What is 2 + 2?</p>", shown.GetProperty("text").GetString()?.Replace("\n", ""), ignoreCase: true);
        Assert.Equal(2, shown.GetProperty("options").GetArrayLength());
        Assert.DoesNotContain("isCorrect", preview.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task APreview_StoresNothing_SoItCreatesNoAttemptAndUsesUpNoAllowance()
    {
        using var admin = await factory.AdminClientAsync();
        var question = await CreateQuestionAsync(admin, "What is 2 + 2?", "4", "5");
        var examId = await CreateExamAsync(admin, "Preview Exam", [question], startsIn: TimeSpan.FromMinutes(-5));
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;

        (await admin.GetAsync($"/v1/exams/{examId}/preview")).EnsureSuccessStatusCode();
        (await admin.GetAsync($"/v1/exams/{examId}/preview")).EnsureSuccessStatusCode();

        var staff = await admin.GetFromJsonAsync<JsonElement>($"/v1/exams/{examId}/attempts");
        Assert.All(staff.GetProperty("candidates").EnumerateArray(), c => Assert.Equal(0, c.GetProperty("attemptsUsed").GetInt32()));
        var mine = (await candidate.GetFromJsonAsync<JsonElement>("/v1/me/exams")).EnumerateArray().Single(e => e.GetProperty("examId").GetGuid() == examId);
        Assert.Equal(0, mine.GetProperty("attemptsUsed").GetInt32());
        Assert.True(mine.GetProperty("canStartAttempt").GetBoolean());
    }

    [Fact]
    public async Task ThePreview_CarriesTheExamsProctoringSettings()
    {
        using var admin = await factory.AdminClientAsync();
        var question = await CreateQuestionAsync(admin, "What is 2 + 2?", "4", "5");
        var examId = await CreateExamAsync(admin, "Preview Exam", [question], startsIn: TimeSpan.FromHours(2), publish: false);
        await admin.PutAsJsonAsync($"/v1/exams/{examId}/focus-violation-limit", new { focusViolationLimit = 4 });
        await admin.PutAsJsonAsync($"/v1/exams/{examId}/content-protection", new { contentProtection = false });

        var preview = await admin.GetFromJsonAsync<JsonElement>($"/v1/exams/{examId}/preview");

        Assert.Equal(4, preview.GetProperty("focusViolationLimit").GetInt32());
        Assert.False(preview.GetProperty("contentProtection").GetBoolean());
    }

    [Fact]
    public async Task AnUnknownExam_Is404()
    {
        using var admin = await factory.AdminClientAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/v1/exams/{Guid.NewGuid()}/preview")).StatusCode);
    }

    [Fact]
    public async Task ACandidate_CannotPreview_AndAnonymousCallersAreRefused()
    {
        using var admin = await factory.AdminClientAsync();
        var question = await CreateQuestionAsync(admin, "What is 2 + 2?", "4", "5");
        var examId = await CreateExamAsync(admin, "Preview Exam", [question], startsIn: TimeSpan.FromMinutes(-5));
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;

        Assert.Equal(HttpStatusCode.Forbidden, (await candidate.GetAsync($"/v1/exams/{examId}/preview")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync($"/v1/exams/{examId}/preview")).StatusCode);
    }
}
