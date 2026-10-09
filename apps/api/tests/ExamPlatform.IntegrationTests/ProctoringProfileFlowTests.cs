using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExamPlatform.Modules.ExamRuntime.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// Proctoring profiles (FR-46): an author applies a preset of the proctoring settings, the notice candidates are shown is written from
/// those settings, and the attempt keeps the text the candidate acknowledged.
/// </summary>
public sealed class ProctoringProfileFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<Guid> PublishedExamAsync(HttpClient admin, bool publish = true)
    {
        var question = await CreateQuestionAsync(admin, "What is 2 + 2?", "4", "5");
        return await CreateExamAsync(admin, "Profile Exam", [question], startsIn: TimeSpan.FromMinutes(-5), publish: publish);
    }

    private static async Task<JsonElement> ExamAsync(HttpClient admin, Guid examId) => await admin.GetFromJsonAsync<JsonElement>($"/v1/exams/{examId}");

    private static string[] Notice(JsonElement exam) =>
        exam.GetProperty("proctoring").GetProperty("notice").EnumerateArray().Select(n => n.GetString()!).ToArray();

    [Fact]
    public async Task TheCatalogue_ListsEveryProfile_WithWhatEachDoesAndWhatCandidatesWouldBeTold()
    {
        using var admin = await factory.AdminClientAsync();

        var profiles = (await admin.GetFromJsonAsync<JsonElement>("/v1/proctoring-profiles")).EnumerateArray().ToList();

        Assert.Equal(["OFF", "BROWSER_LOCK", "BROWSER_CAMERA", "FULL"], profiles.Select(p => p.GetProperty("id").GetString()));
        var browserLock = profiles.Single(p => p.GetProperty("id").GetString() == "BROWSER_LOCK");
        Assert.True(browserLock.GetProperty("available").GetBoolean());
        Assert.True(browserLock.GetProperty("contentProtection").GetBoolean());
        Assert.Equal(5, browserLock.GetProperty("focusViolationLimit").GetInt32());
        Assert.Contains(browserLock.GetProperty("notice").EnumerateArray(), n => n.GetString()!.Contains("If you leave 5 times"));
        var camera = profiles.Single(p => p.GetProperty("id").GetString() == "BROWSER_CAMERA");
        Assert.False(camera.GetProperty("available").GetBoolean());
        Assert.False(string.IsNullOrEmpty(camera.GetProperty("unavailableReason").GetString()));
    }

    [Fact]
    public async Task AnExamsDefaultSettings_AreReportedAsCustom_WithTheNoticeWrittenFromThem()
    {
        using var admin = await factory.AdminClientAsync();
        var examId = await PublishedExamAsync(admin);

        var exam = await ExamAsync(admin, examId);

        Assert.Equal("CUSTOM", exam.GetProperty("proctoring").GetProperty("profile").GetString());
        Assert.Contains(Notice(exam), n => n.StartsWith("Copying, pasting"));
        Assert.DoesNotContain(Notice(exam), n => n.Contains("Stay on the exam page"));
    }

    [Fact]
    public async Task ApplyingBrowserLock_SetsBothSettings_AndTheNoticeFollows_EvenAfterPublishing()
    {
        using var admin = await factory.AdminClientAsync();
        var examId = await PublishedExamAsync(admin);

        var response = await admin.PutAsJsonAsync($"/v1/exams/{examId}/proctoring-profile", new { profile = "BROWSER_LOCK" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var exam = await ExamAsync(admin, examId);
        Assert.Equal("BROWSER_LOCK", exam.GetProperty("proctoring").GetProperty("profile").GetString());
        Assert.Equal("Browser lock", exam.GetProperty("proctoring").GetProperty("profileName").GetString());
        Assert.True(exam.GetProperty("config").GetProperty("contentProtection").GetBoolean());
        Assert.Equal(5, exam.GetProperty("config").GetProperty("focusViolationLimit").GetInt32());
        Assert.Contains(Notice(exam), n => n.Contains("If you leave 5 times"));

        await admin.PutAsJsonAsync($"/v1/exams/{examId}/proctoring-profile", new { profile = "off" });
        exam = await ExamAsync(admin, examId);
        Assert.Equal("OFF", exam.GetProperty("proctoring").GetProperty("profile").GetString());
        Assert.False(exam.GetProperty("config").GetProperty("contentProtection").GetBoolean());
        Assert.Equal(0, exam.GetProperty("config").GetProperty("focusViolationLimit").GetInt32());
        Assert.DoesNotContain(Notice(exam), n => n.StartsWith("Copying"));
    }

    [Fact]
    public async Task TuningASettingAfterAProfile_MakesTheExamCustom_AndTheNoticeFollowsThat()
    {
        using var admin = await factory.AdminClientAsync();
        var examId = await PublishedExamAsync(admin, publish: false);
        await admin.PutAsJsonAsync($"/v1/exams/{examId}/proctoring-profile", new { profile = "BROWSER_LOCK" });

        await admin.PutAsJsonAsync($"/v1/exams/{examId}/focus-violation-limit", new { focusViolationLimit = 2 });

        var exam = await ExamAsync(admin, examId);
        Assert.Equal("CUSTOM", exam.GetProperty("proctoring").GetProperty("profile").GetString());
        Assert.Contains(Notice(exam), n => n.Contains("If you leave 2 times"));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"profile\": \"\"}")]
    [InlineData("{\"profile\": \"NOPE\"}")]
    [InlineData("{\"profile\": \"BROWSER_CAMERA\"}")]
    [InlineData("{\"profile\": \"FULL\"}")]
    public async Task AMissingUnknownOrNotYetBuiltProfile_Is400_AndNothingChanges(string body)
    {
        using var admin = await factory.AdminClientAsync();
        var examId = await PublishedExamAsync(admin);

        var response = await admin.PutAsync($"/v1/exams/{examId}/proctoring-profile", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("CUSTOM", (await ExamAsync(admin, examId)).GetProperty("proctoring").GetProperty("profile").GetString());
    }

    [Fact]
    public async Task AnUnknownExam_Is404()
    {
        using var admin = await factory.AdminClientAsync();

        var response = await admin.PutAsJsonAsync($"/v1/exams/{Guid.NewGuid()}/proctoring-profile", new { profile = "OFF" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ACandidate_CannotApplyAProfile_NorListThem()
    {
        using var admin = await factory.AdminClientAsync();
        var examId = await PublishedExamAsync(admin);
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;

        Assert.Equal(HttpStatusCode.Forbidden, (await candidate.PutAsJsonAsync($"/v1/exams/{examId}/proctoring-profile", new { profile = "OFF" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await candidate.GetAsync("/v1/proctoring-profiles")).StatusCode);
    }

    [Fact]
    public async Task TheCandidatesInstructions_CarryTheNoticeWrittenFromTheExamsSettings()
    {
        using var admin = await factory.AdminClientAsync();
        var examId = await PublishedExamAsync(admin);
        await admin.PutAsJsonAsync($"/v1/exams/{examId}/proctoring-profile", new { profile = "BROWSER_LOCK" });
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;

        var mine = (await candidate.GetFromJsonAsync<JsonElement>("/v1/me/exams")).EnumerateArray().Single(e => e.GetProperty("examId").GetGuid() == examId);

        var notice = mine.GetProperty("rules").GetProperty("proctoringNotice").EnumerateArray().Select(n => n.GetString()!).ToArray();
        Assert.Equal(Notice(await ExamAsync(admin, examId)), notice);
        Assert.Contains(notice, n => n.Contains("IP address"));
        Assert.Contains(notice, n => n.Contains("If you leave 5 times"));
    }

    [Fact]
    public async Task TheAttempt_KeepsTheNoticeTheCandidateAcknowledged_EvenIfTheExamChangesLater()
    {
        using var admin = await factory.AdminClientAsync();
        var examId = await PublishedExamAsync(admin);
        await admin.PutAsJsonAsync($"/v1/exams/{examId}/proctoring-profile", new { profile = "BROWSER_LOCK" });
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;

        var start = await candidate.PostAsJsonAsync($"/v1/me/exams/{examId}/attempts", new { instructionsAcknowledged = true });
        start.EnsureSuccessStatusCode();
        var attemptId = (await start.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await admin.PutAsJsonAsync($"/v1/exams/{examId}/proctoring-profile", new { profile = "OFF" });

        using var scope = factory.Services.CreateScope();
        var attempt = await scope.ServiceProvider.GetRequiredService<ExamRuntimeDbContext>().Attempts.AsNoTracking().SingleAsync(a => a.Id == attemptId);
        Assert.NotNull(attempt.AcknowledgedNotice);
        Assert.Contains("Copying, pasting, right-click and printing are turned off", attempt.AcknowledgedNotice);
        Assert.Contains("If you leave 5 times", attempt.AcknowledgedNotice);
    }
}
