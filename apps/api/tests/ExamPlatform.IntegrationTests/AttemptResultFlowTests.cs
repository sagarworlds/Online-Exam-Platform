using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// The result page (FR-32) over real HTTP and a real database: a candidate's score is placed among the exam's other candidates' best
/// released results, a retake counts once, and nobody sees a result before the exam's author releases it.
/// </summary>
public sealed class AttemptResultFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await JsonAsync(response)).GetProperty("title").GetString());
    }

    /// <summary>A published exam of two questions, open now, with an administrator client.</summary>
    private async Task<(HttpClient Admin, Guid ExamId)> OpenExamAsync()
    {
        var admin = await factory.AdminClientAsync();
        var questions = new List<Guid>
        {
            await CreateQuestionAsync(admin, "Q1", "Right", "Wrong"),
            await CreateQuestionAsync(admin, "Q2", "Right", "Wrong"),
        };
        var examId = await CreateExamAsync(admin, "Ranked exam", questions, TimeSpan.FromMinutes(-5));
        return (admin, examId);
    }

    private static async Task<Guid> StartAsync(HttpClient candidate, Guid examId) =>
        (await JsonAsync((await candidate.PostAsJsonAsync($"/v1/me/exams/{examId}/attempts", new { instructionsAcknowledged = true })).EnsureSuccessStatusCode()))
        .GetProperty("id").GetGuid();

    /// <summary>Starts an attempt, answers Q1 with the right option when asked to, and submits; returns the attempt's id.</summary>
    private static async Task<Guid> SitAsync(HttpClient candidate, Guid examId, bool answerRight)
    {
        var attemptId = await StartAsync(candidate, examId);
        if (answerRight)
        {
            var attempt = await candidate.GetFromJsonAsync<JsonElement>($"/v1/me/attempts/{attemptId}");
            var first = attempt.GetProperty("sections").EnumerateArray().SelectMany(s => s.GetProperty("questions").EnumerateArray())
                .Single(q => q.GetProperty("text").GetString() == "Q1");
            var right = first.GetProperty("options").EnumerateArray().Single(o => o.GetProperty("text").GetString() == "Right").GetProperty("id").GetGuid();
            (await candidate.PutAsJsonAsync($"/v1/me/attempts/{attemptId}/answers/{first.GetProperty("id").GetGuid()}", new { optionId = right }))
                .EnsureSuccessStatusCode();
        }

        (await candidate.PostAsync($"/v1/me/attempts/{attemptId}/submit", content: null)).EnsureSuccessStatusCode();
        return attemptId;
    }

    private static async Task<HttpResponseMessage> ResultAsync(HttpClient candidate, Guid attemptId) =>
        await candidate.GetAsync($"/v1/me/attempts/{attemptId}/result");

    [Fact]
    public async Task OnAnInstantExam_TheResultIsPlaced_AmongTheCandidatesWhoHaveFinished()
    {
        var (admin, examId) = await OpenExamAsync();
        using var _a = admin;
        var (first, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _f = first;
        var (second, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _s = second;

        // Alone on the board, the first finisher is first of one.
        var firstAttempt = await SitAsync(first, examId, answerRight: true);
        var alone = await JsonAsync((await ResultAsync(first, firstAttempt)).EnsureSuccessStatusCode());
        Assert.Equal(1, alone.GetProperty("rank").GetInt32());
        Assert.Equal(1, alone.GetProperty("cohortSize").GetInt32());
        Assert.Equal(100m, alone.GetProperty("percentile").GetDecimal());
        Assert.True(alone.GetProperty("provisional").GetBoolean(), "the window is still open, so the rank can still move");

        // The second finisher scores lower, so is second of two, and the first finisher is still first once the board has two.
        var secondAttempt = await SitAsync(second, examId, answerRight: false);
        var placed = await JsonAsync((await ResultAsync(second, secondAttempt)).EnsureSuccessStatusCode());
        Assert.Equal(2, placed.GetProperty("rank").GetInt32());
        Assert.Equal(2, placed.GetProperty("cohortSize").GetInt32());
        Assert.Equal(50m, placed.GetProperty("percentile").GetDecimal());

        var firstNow = await JsonAsync((await ResultAsync(first, firstAttempt)).EnsureSuccessStatusCode());
        Assert.Equal(1, firstNow.GetProperty("rank").GetInt32());
        Assert.Equal(2, firstNow.GetProperty("cohortSize").GetInt32());
    }

    [Fact]
    public async Task ASectionBreakdown_CountsEachVerdict_AndCarriesNoAnswerKey()
    {
        var (admin, examId) = await OpenExamAsync();
        using var _a = admin;
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;
        var attemptId = await SitAsync(candidate, examId, answerRight: true);

        var raw = await (await ResultAsync(candidate, attemptId)).EnsureSuccessStatusCode().Content.ReadAsStringAsync();
        var result = JsonDocument.Parse(raw).RootElement;

        Assert.Equal(1, result.GetProperty("correctCount").GetInt32());
        Assert.Equal(1, result.GetProperty("unansweredCount").GetInt32());
        var section = Assert.Single(result.GetProperty("sections").EnumerateArray());
        Assert.Equal(1, section.GetProperty("correctCount").GetInt32());
        Assert.Equal(1, section.GetProperty("unansweredCount").GetInt32());
        Assert.DoesNotContain("isCorrect", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("options", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ARetake_CountsOnceOnTheBoard_ByItsBestAttempt()
    {
        var (admin, examId) = await OpenExamAsync();
        using var _a = admin;
        var (retaker, retakerUser) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _r = retaker;
        var (other, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _o = other;

        await SitAsync(retaker, examId, answerRight: true);
        (await admin.PostAsJsonAsync($"/v1/exams/{examId}/candidates/{retakerUser.UserId}/extra-attempts", new { reason = "Retake" })).EnsureSuccessStatusCode();
        await SitAsync(retaker, examId, answerRight: false);

        // The retaker's weaker second attempt is not a second entry: the board holds two candidates, not three attempts. The
        // other candidate ranks below the retaker's better attempt.
        var otherAttempt = await SitAsync(other, examId, answerRight: false);
        var placed = await JsonAsync((await ResultAsync(other, otherAttempt)).EnsureSuccessStatusCode());
        Assert.Equal(2, placed.GetProperty("cohortSize").GetInt32());
        Assert.Equal(2, placed.GetProperty("rank").GetInt32());
    }

    [Fact]
    public async Task AnInvalidatedResult_HasNoResult_AndIsNotPlacedOnTheBoard()
    {
        var (admin, examId) = await OpenExamAsync();
        using var _a = admin;
        var (cheat, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = cheat;
        var (honest, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _h = honest;

        var invalidated = await SitAsync(cheat, examId, answerRight: true);
        (await admin.PostAsJsonAsync($"/v1/exams/{examId}/attempts/{invalidated}/invalidate", new { reason = "Shared answers" })).EnsureSuccessStatusCode();
        await AssertProblemAsync(await ResultAsync(cheat, invalidated), HttpStatusCode.Conflict, "attempt_invalidated");

        var honestAttempt = await SitAsync(honest, examId, answerRight: false);
        var placed = await JsonAsync((await ResultAsync(honest, honestAttempt)).EnsureSuccessStatusCode());
        Assert.Equal(1, placed.GetProperty("cohortSize").GetInt32());
    }

    [Fact]
    public async Task BeforeAScheduledRelease_TheResultIsRefused()
    {
        var (admin, examId) = await OpenExamAsync();
        using var _a = admin;
        (await admin.PutAsJsonAsync($"/v1/exams/{examId}/result-release", new { mode = "Scheduled", releaseTime = DateTime.UtcNow.AddDays(2) })).EnsureSuccessStatusCode();
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;
        var attemptId = await SitAsync(candidate, examId, answerRight: true);

        await AssertProblemAsync(await ResultAsync(candidate, attemptId), HttpStatusCode.Conflict, "results_not_released");
    }

    [Fact]
    public async Task SomeoneElsesAttempt_HasNoResult_ForAnotherCandidateOrAnAdministrator()
    {
        var (admin, examId) = await OpenExamAsync();
        using var _a = admin;
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;
        var attemptId = await SitAsync(candidate, examId, answerRight: true);
        var (other, _) = await factory.CandidateClientAsync();
        using var _o = other;

        await AssertProblemAsync(await ResultAsync(other, attemptId), HttpStatusCode.NotFound, "attempt_not_found");
        await AssertProblemAsync(await ResultAsync(admin, attemptId), HttpStatusCode.NotFound, "attempt_not_found");
    }

    [Fact]
    public async Task AnOpenAttempt_HasNoResultYet()
    {
        var (admin, examId) = await OpenExamAsync();
        using var _a = admin;
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;
        var attemptId = await StartAsync(candidate, examId);

        await AssertProblemAsync(await ResultAsync(candidate, attemptId), HttpStatusCode.Conflict, "attempt_not_submitted");
    }
}
