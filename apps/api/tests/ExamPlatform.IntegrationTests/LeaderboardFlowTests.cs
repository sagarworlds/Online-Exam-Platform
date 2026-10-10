using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Text.Json;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// The leaderboards (FR-35) over real HTTP and a real database: a candidate sees the exam's ranking once the results are released, with other
/// candidates' names shortened, and sees only the batches they are in.
/// </summary>
public sealed partial class LeaderboardFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    /// <summary>A name as other candidates may see it: a first name, optionally followed by an initial, never a whole surname.</summary>
    [GeneratedRegex(@"^\S+( \S\.)?$")]
    private static partial Regex ShortenedName();

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await JsonAsync(response)).GetProperty("title").GetString());
    }

    private static async Task<Guid> SitRightAsync(HttpClient candidate, Guid examId, Guid questionId)
    {
        var started = await candidate.PostAsJsonAsync($"/v1/me/exams/{examId}/attempts", new { instructionsAcknowledged = true });
        var attemptId = (await JsonAsync(started.EnsureSuccessStatusCode())).GetProperty("id").GetGuid();
        var attempt = await candidate.GetFromJsonAsync<JsonElement>($"/v1/me/attempts/{attemptId}");
        var right = attempt.GetProperty("sections").EnumerateArray().SelectMany(s => s.GetProperty("questions").EnumerateArray())
            .Single(q => q.GetProperty("id").GetGuid() == questionId)
            .GetProperty("options").EnumerateArray().Single(o => o.GetProperty("text").GetString() == "Right").GetProperty("id").GetGuid();
        (await candidate.PutAsJsonAsync($"/v1/me/attempts/{attemptId}/answers/{questionId}", new { optionId = right })).EnsureSuccessStatusCode();
        (await candidate.PostAsync($"/v1/me/attempts/{attemptId}/submit", content: null)).EnsureSuccessStatusCode();
        return attemptId;
    }

    /// <summary>Starts and submits an attempt without answering, so the candidate is on the board with the lowest place.</summary>
    private static async Task SitBlankAsync(HttpClient candidate, Guid examId)
    {
        var started = await candidate.PostAsJsonAsync($"/v1/me/exams/{examId}/attempts", new { instructionsAcknowledged = true });
        var attemptId = (await JsonAsync(started.EnsureSuccessStatusCode())).GetProperty("id").GetGuid();
        (await candidate.PostAsync($"/v1/me/attempts/{attemptId}/submit", content: null)).EnsureSuccessStatusCode();
    }

    /// <summary>An exam of one question, with two enrolled candidates who have both sat it: the first right, the second blank.</summary>
    private async Task<(HttpClient Admin, Guid ExamId, Guid QuestionId, HttpClient First, HttpClient Second)> SittenExamAsync()
    {
        var admin = await factory.AdminClientAsync();
        var question = await CreateQuestionAsync(admin, "Leaderboard question", "Right", "Wrong");
        var examId = await CreateExamAsync(admin, "Ranked exam", new List<Guid> { question }, TimeSpan.FromMinutes(-5));
        var (first, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        var (second, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        await SitRightAsync(first, examId, question);
        await SitBlankAsync(second, examId);
        return (admin, examId, question, first, second);
    }

    [Fact]
    public async Task OnceReleased_TheOverallBoard_ListsEveryCandidate_WithShortenedNames_AndMarksTheCandidatesOwnRow()
    {
        var (admin, examId, _, first, second) = await SittenExamAsync();
        using var _a = admin;
        using var _f = first;
        using var _s = second;

        var board = await JsonAsync((await second.GetAsync($"/v1/me/exams/{examId}/leaderboard")).EnsureSuccessStatusCode());

        Assert.Equal("overall", board.GetProperty("board").GetString());
        Assert.Equal(2, board.GetProperty("candidateCount").GetInt32());
        var entries = board.GetProperty("entries").EnumerateArray().ToList();
        Assert.Equal(2, entries.Count);
        Assert.Equal(1, entries[0].GetProperty("rank").GetInt32());
        foreach (var entry in entries)
        {
            var name = entry.GetProperty("name");
            if (name.ValueKind == JsonValueKind.String)
                Assert.True(ShortenedName().IsMatch(name.GetString()!), $"'{name.GetString()}' shows more than a first name and an initial");
        }

        Assert.Single(entries, e => e.GetProperty("isYou").GetBoolean());
    }

    [Fact]
    public async Task BeforeTheResultsAreReleased_NoBoardIsShown()
    {
        var (admin, examId, _, first, second) = await SittenExamAsync();
        using var _a = admin;
        using var _f = first;
        using var _s = second;
        (await admin.PutAsJsonAsync($"/v1/exams/{examId}/result-release", new { mode = "Scheduled", releaseTime = DateTime.UtcNow.AddDays(2) })).EnsureSuccessStatusCode();

        await AssertProblemAsync(await second.GetAsync($"/v1/me/exams/{examId}/leaderboard"), HttpStatusCode.Conflict, "results_not_released");
    }

    [Fact]
    public async Task AnUnknownBoard_IsRefused_AsABadRequest()
    {
        var (admin, examId, _, first, second) = await SittenExamAsync();
        using var _a = admin;
        using var _f = first;
        using var _s = second;

        await AssertProblemAsync(await second.GetAsync($"/v1/me/exams/{examId}/leaderboard?board=everyone"), HttpStatusCode.BadRequest, "invalid_leaderboard_board");
    }

    [Fact]
    public async Task ABatchTheCandidateIsNotIn_IsNotFound_SoItsRankingCannotBeProbed()
    {
        var (admin, examId, _, first, second) = await SittenExamAsync();
        using var _a = admin;
        using var _f = first;
        using var _s = second;

        await AssertProblemAsync(
            await second.GetAsync($"/v1/me/exams/{examId}/leaderboard?board=batch&batchId={Guid.NewGuid()}"),
            HttpStatusCode.NotFound,
            "leaderboard_batch_not_found");
    }

    [Fact]
    public async Task ACandidateNotEnrolledInTheExam_SeesNoBoard()
    {
        var (admin, examId, _, first, second) = await SittenExamAsync();
        using var _a = admin;
        using var _f = first;
        using var _s = second;
        var (stranger, _) = await factory.CandidateClientAsync();
        using var _x = stranger;

        await AssertProblemAsync(await stranger.GetAsync($"/v1/me/exams/{examId}/leaderboard"), HttpStatusCode.NotFound, "candidate_not_enrolled");
    }
}
