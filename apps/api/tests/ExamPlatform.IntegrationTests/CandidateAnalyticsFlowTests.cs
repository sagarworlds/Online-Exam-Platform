using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// The candidate's own analytics (FR-36) over real HTTP and a real database: they show the candidate's released results only, and
/// another candidate's results never appear in them.
/// </summary>
public sealed class CandidateAnalyticsFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    /// <summary>A published exam of two questions, open now, with an administrator client.</summary>
    private async Task<(HttpClient Admin, Guid ExamId)> OpenExamAsync(string name)
    {
        var admin = await factory.AdminClientAsync();
        var questions = new List<Guid>
        {
            await CreateQuestionAsync(admin, "Q1", "Right", "Wrong"),
            await CreateQuestionAsync(admin, "Q2", "Right", "Wrong"),
        };
        var examId = await CreateExamAsync(admin, name, questions, TimeSpan.FromMinutes(-5));
        return (admin, examId);
    }

    /// <summary>Starts an attempt, answers Q1 with the right option, and submits it.</summary>
    private static async Task SitRightOnFirstAsync(HttpClient candidate, Guid examId)
    {
        var started = await candidate.PostAsJsonAsync($"/v1/me/exams/{examId}/attempts", new { instructionsAcknowledged = true });
        started.EnsureSuccessStatusCode();
        var attemptId = (await JsonAsync(started)).GetProperty("id").GetGuid();

        var attempt = await candidate.GetFromJsonAsync<JsonElement>($"/v1/me/attempts/{attemptId}");
        var first = attempt.GetProperty("sections").EnumerateArray().SelectMany(s => s.GetProperty("questions").EnumerateArray())
            .Single(q => q.GetProperty("text").GetString() == "Q1");
        var right = first.GetProperty("options").EnumerateArray().Single(o => o.GetProperty("text").GetString() == "Right").GetProperty("id").GetGuid();
        (await candidate.PutAsJsonAsync($"/v1/me/attempts/{attemptId}/answers/{first.GetProperty("id").GetGuid()}", new { optionId = right }))
            .EnsureSuccessStatusCode();
        (await candidate.PostAsync($"/v1/me/attempts/{attemptId}/submit", content: null)).EnsureSuccessStatusCode();
    }

    private static async Task<JsonElement> AnalyticsAsync(HttpClient candidate) =>
        await JsonAsync((await candidate.GetAsync("/v1/me/analytics")).EnsureSuccessStatusCode());

    [Fact]
    public async Task AfterAnInstantExam_TheTrendAndTheSectionsShowTheResult()
    {
        var (admin, examId) = await OpenExamAsync("Analytics exam");
        using var _a = admin;
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;
        await SitRightOnFirstAsync(candidate, examId);

        var analytics = await AnalyticsAsync(candidate);

        Assert.Equal(1, analytics.GetProperty("resultCount").GetInt32());
        var point = Assert.Single(analytics.GetProperty("trend").EnumerateArray());
        Assert.Equal(examId, point.GetProperty("examId").GetGuid());
        var section = Assert.Single(analytics.GetProperty("sections").EnumerateArray());
        Assert.Equal(1, section.GetProperty("correctCount").GetInt32());
        Assert.Equal(1, section.GetProperty("unansweredCount").GetInt32());
        Assert.Equal(100m, section.GetProperty("accuracy").GetDecimal());
    }

    [Fact]
    public async Task WhileTheAuthorHoldsTheResults_TheCandidateSeesNoTrendOrSections()
    {
        var (admin, examId) = await OpenExamAsync("Held exam");
        using var _a = admin;
        (await admin.PutAsJsonAsync($"/v1/exams/{examId}/result-release", new { mode = "Manual" })).EnsureSuccessStatusCode();
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;
        await SitRightOnFirstAsync(candidate, examId);

        var analytics = await AnalyticsAsync(candidate);

        Assert.Equal(0, analytics.GetProperty("resultCount").GetInt32());
        Assert.Empty(analytics.GetProperty("trend").EnumerateArray());
        Assert.Empty(analytics.GetProperty("sections").EnumerateArray());
    }

    [Fact]
    public async Task AnotherCandidatesResults_AreNeverShown()
    {
        var (admin, examId) = await OpenExamAsync("Shared exam");
        using var _a = admin;
        var (sitter, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _s = sitter;
        var (bystander, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _b = bystander;
        await SitRightOnFirstAsync(sitter, examId);

        var analytics = await AnalyticsAsync(bystander);

        Assert.Equal(0, analytics.GetProperty("resultCount").GetInt32());
    }

    [Fact]
    public async Task TheAnalyticsRoute_NeedsASignedInCandidate()
    {
        using var anonymous = factory.CreateClient();

        var response = await anonymous.GetAsync("/v1/me/analytics");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
