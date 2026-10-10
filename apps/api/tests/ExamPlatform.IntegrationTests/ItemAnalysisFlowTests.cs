using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// The staff item analysis (FR-37) over real HTTP and a real database: it counts released results only, names each question by its opening
/// words, and withholds the difficulty and discrimination indices until enough candidates have had a question.
/// </summary>
public sealed class ItemAnalysisFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

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

    private static async Task<JsonElement> AnalysisAsync(HttpClient admin, Guid examId) =>
        await JsonAsync((await admin.GetAsync($"/v1/exams/{examId}/analytics/items")).EnsureSuccessStatusCode());

    [Fact]
    public async Task AReleasedExam_ListsEachQuestion_AndCountsTheAnswers()
    {
        var (admin, examId) = await OpenExamAsync("Item exam");
        using var _a = admin;
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;
        await SitRightOnFirstAsync(candidate, examId);

        var analysis = await AnalysisAsync(admin, examId);

        Assert.True(analysis.GetProperty("resultsReleased").GetBoolean());
        Assert.Equal(1, analysis.GetProperty("candidateCount").GetInt32());
        var questions = analysis.GetProperty("questions").EnumerateArray().ToList();
        Assert.Equal(2, questions.Count);
        Assert.Equal("Q1", questions[0].GetProperty("text").GetString());
        Assert.Equal(1, questions[0].GetProperty("correctCount").GetInt32());
        Assert.Equal(1, questions[0].GetProperty("attempts").GetInt32());
    }

    [Fact]
    public async Task BelowTheMinimumCohort_TheIndicesAreWithheld_NotGuessed()
    {
        // One candidate is far below the default minimum cohort, so the counts show and the indices are null rather than a figure from one person.
        var (admin, examId) = await OpenExamAsync("Small exam");
        using var _a = admin;
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;
        await SitRightOnFirstAsync(candidate, examId);

        var analysis = await AnalysisAsync(admin, examId);

        Assert.True(analysis.GetProperty("minimumCohortSize").GetInt32() >= 10);
        var first = analysis.GetProperty("questions")[0];
        Assert.Equal(JsonValueKind.Null, first.GetProperty("difficulty").ValueKind);
        Assert.Equal(JsonValueKind.Null, first.GetProperty("discrimination").ValueKind);
    }

    [Fact]
    public async Task AHeldExam_HasNoQuestions_AndSaysItsResultsAreHeld()
    {
        var (admin, examId) = await OpenExamAsync("Held item exam");
        using var _a = admin;
        (await admin.PutAsJsonAsync($"/v1/exams/{examId}/result-release", new { mode = "Manual" })).EnsureSuccessStatusCode();
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;
        await SitRightOnFirstAsync(candidate, examId);

        var analysis = await AnalysisAsync(admin, examId);

        Assert.False(analysis.GetProperty("resultsReleased").GetBoolean());
        Assert.Empty(analysis.GetProperty("questions").EnumerateArray());
    }

    [Fact]
    public async Task AnUnknownExam_Returns404()
    {
        using var admin = await factory.AdminClientAsync();

        var response = await admin.GetAsync($"/v1/exams/{Guid.NewGuid()}/analytics/items");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
