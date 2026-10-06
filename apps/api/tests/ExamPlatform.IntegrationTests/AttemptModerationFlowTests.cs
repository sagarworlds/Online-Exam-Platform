using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExamPlatform.Modules.Admin.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// What an administrator can do to a candidate's attempt (FR-29): warn, pause, resume, terminate and invalidate, and what the
/// candidate's page then sees.
/// </summary>
public sealed class AttemptModerationFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record Sitting(HttpClient Admin, HttpClient Candidate, Guid ExamId, Guid AttemptId, Guid QuestionId, Guid CorrectOptionId);

    private async Task<Sitting> StartSittingAsync()
    {
        var admin = await factory.AdminClientAsync();
        var question = await CreateQuestionAsync(admin, "What is 2 + 2?", "4", "5");
        var examId = await CreateExamAsync(admin, "Moderation Exam", [question], startsIn: TimeSpan.FromMinutes(-5));
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);

        var response = await candidate.PostAsJsonAsync($"/v1/me/exams/{examId}/attempts", new { instructionsAcknowledged = true });
        response.EnsureSuccessStatusCode();
        var attempt = await response.Content.ReadFromJsonAsync<JsonElement>();
        var first = attempt.GetProperty("sections")[0].GetProperty("questions")[0];
        var optionId = first.GetProperty("options").EnumerateArray().Single(o => o.GetProperty("text").GetString() == "4").GetProperty("id").GetGuid();

        return new Sitting(admin, candidate, examId, attempt.GetProperty("id").GetGuid(), first.GetProperty("id").GetGuid(), optionId);
    }

    private static string Route(Sitting s, string action) => $"/v1/exams/{s.ExamId}/attempts/{s.AttemptId}/{action}";

    private static Task<HttpResponseMessage> AnswerAsync(Sitting s) =>
        s.Candidate.PutAsJsonAsync($"/v1/me/attempts/{s.AttemptId}/answers/{s.QuestionId}", new { optionId = s.CorrectOptionId });

    private static Task<JsonElement> StatusAsync(Sitting s) => s.Candidate.GetFromJsonAsync<JsonElement>($"/v1/me/attempts/{s.AttemptId}/status");

    private static Task<JsonElement> AttemptAsync(Sitting s) => s.Candidate.GetFromJsonAsync<JsonElement>($"/v1/me/attempts/{s.AttemptId}");

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString();

    [Fact]
    public async Task AWarning_ShowsOnTheCandidatesHeartbeatAndAttempt_AndTheStaffRowCountsIt()
    {
        var s = await StartSittingAsync();
        using var _a = s.Admin;
        using var _c = s.Candidate;

        var sent = await s.Admin.PostAsJsonAsync(Route(s, "warn"), new { message = "Eyes on your own screen, please." });

        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        Assert.Equal(1, (await sent.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("warnings").GetInt32());
        var warnings = (await StatusAsync(s)).GetProperty("warnings");
        Assert.Equal("Eyes on your own screen, please.", Assert.Single(warnings.EnumerateArray()).GetProperty("message").GetString());
        Assert.Single((await AttemptAsync(s)).GetProperty("warnings").EnumerateArray());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"message\": \"  \"}")]
    public async Task AWarningWithoutText_Is400(string body)
    {
        var s = await StartSittingAsync();
        using var _a = s.Admin;
        using var _c = s.Candidate;

        var response = await s.Admin.PostAsync(Route(s, "warn"), new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty((await StatusAsync(s)).GetProperty("warnings").EnumerateArray());
    }

    [Fact]
    public async Task Pausing_StopsTheCandidateAnswering_AndTheHeartbeatSaysSo_ThenResumingGivesBackTheTime()
    {
        var s = await StartSittingAsync();
        using var _a = s.Admin;
        using var _c = s.Candidate;
        var deadlineBefore = DateTime.Parse((await StatusAsync(s)).GetProperty("deadlineUtc").GetString()!).ToUniversalTime();

        var paused = await s.Admin.PostAsync(Route(s, "pause"), null);

        Assert.Equal(HttpStatusCode.OK, paused.StatusCode);
        Assert.True((await paused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("paused").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, (await StatusAsync(s)).GetProperty("pausedAtUtc").ValueKind);
        var refused = await AnswerAsync(s);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("attempt_paused", await ErrorCodeAsync(refused));
        var submit = await s.Candidate.PostAsync($"/v1/me/attempts/{s.AttemptId}/submit", null);
        Assert.Equal("attempt_paused", await ErrorCodeAsync(submit));

        await Task.Delay(1200);
        var resumed = await s.Admin.PostAsync(Route(s, "resume"), null);

        Assert.Equal(HttpStatusCode.OK, resumed.StatusCode);
        var status = await StatusAsync(s);
        Assert.Equal(JsonValueKind.Null, status.GetProperty("pausedAtUtc").ValueKind);
        Assert.True(DateTime.Parse(status.GetProperty("deadlineUtc").GetString()!).ToUniversalTime() > deadlineBefore);
        Assert.Equal(HttpStatusCode.NoContent, (await AnswerAsync(s)).StatusCode);
    }

    [Fact]
    public async Task PausingTwice_AndResumingWhenNotPaused_Are409()
    {
        var s = await StartSittingAsync();
        using var _a = s.Admin;
        using var _c = s.Candidate;

        Assert.Equal("attempt_not_paused", await ErrorCodeAsync(await s.Admin.PostAsync(Route(s, "resume"), null)));
        await s.Admin.PostAsync(Route(s, "pause"), null);
        var again = await s.Admin.PostAsync(Route(s, "pause"), null);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("attempt_already_paused", await ErrorCodeAsync(again));
    }

    [Fact]
    public async Task Terminating_EndsTheAttemptWithItsSavedAnswers_AndTheCandidateIsToldWhy()
    {
        var s = await StartSittingAsync();
        using var _a = s.Admin;
        using var _c = s.Candidate;
        await AnswerAsync(s);

        var ended = await s.Admin.PostAsJsonAsync(Route(s, "terminate"), new { reason = "Caught using a phone." });

        Assert.Equal(HttpStatusCode.OK, ended.StatusCode);
        var attempt = await AttemptAsync(s);
        Assert.Equal("Submitted", attempt.GetProperty("status").GetString());
        Assert.True(attempt.GetProperty("terminatedByAdmin").GetBoolean());
        Assert.True(attempt.GetProperty("autoSubmitted").GetBoolean());
        Assert.Equal("Caught using a phone.", attempt.GetProperty("terminationReason").GetString());
        Assert.Equal(1m, attempt.GetProperty("score").GetDecimal());
        Assert.Equal("Submitted", (await StatusAsync(s)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task APausedAttempt_CanBeTerminated_AndTerminatingNeedsAReason()
    {
        var s = await StartSittingAsync();
        using var _a = s.Admin;
        using var _c = s.Candidate;
        await s.Admin.PostAsync(Route(s, "pause"), null);

        var noReason = await s.Admin.PostAsJsonAsync(Route(s, "terminate"), new { });
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        Assert.Equal("InProgress", (await AttemptAsync(s)).GetProperty("status").GetString());

        Assert.Equal(HttpStatusCode.OK, (await s.Admin.PostAsJsonAsync(Route(s, "terminate"), new { reason = "Left the room." })).StatusCode);
        Assert.Equal("Submitted", (await AttemptAsync(s)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Invalidating_HidesTheScoreAndReviewFromTheCandidate_ButStaffStillSeeTheScore()
    {
        var s = await StartSittingAsync();
        using var _a = s.Admin;
        using var _c = s.Candidate;
        await AnswerAsync(s);
        (await s.Candidate.PostAsync($"/v1/me/attempts/{s.AttemptId}/submit", null)).EnsureSuccessStatusCode();

        var invalidated = await s.Admin.PostAsJsonAsync(Route(s, "invalidate"), new { reason = "Answers shared with another candidate." });

        Assert.Equal(HttpStatusCode.OK, invalidated.StatusCode);
        var attempt = await AttemptAsync(s);
        Assert.True(attempt.GetProperty("invalidated").GetBoolean());
        Assert.Equal("Answers shared with another candidate.", attempt.GetProperty("invalidationReason").GetString());
        Assert.Equal(JsonValueKind.Null, attempt.GetProperty("score").ValueKind);
        var review = await s.Candidate.GetAsync($"/v1/me/attempts/{s.AttemptId}/review");
        Assert.Equal(HttpStatusCode.Conflict, review.StatusCode);
        Assert.Equal("attempt_invalidated", await ErrorCodeAsync(review));

        var mine = (await s.Candidate.GetFromJsonAsync<JsonElement>("/v1/me/exams")).EnumerateArray().Single(e => e.GetProperty("examId").GetGuid() == s.ExamId);
        Assert.Equal(JsonValueKind.Null, mine.GetProperty("score").ValueKind);

        var staff = await s.Admin.GetFromJsonAsync<JsonElement>($"/v1/exams/{s.ExamId}/attempts");
        var row = staff.GetProperty("candidates").EnumerateArray().SelectMany(c => c.GetProperty("attempts").EnumerateArray()).Single();
        Assert.True(row.GetProperty("invalidated").GetBoolean());
        Assert.Equal(1m, row.GetProperty("score").GetDecimal());
    }

    [Fact]
    public async Task AnOpenAttempt_CannotBeInvalidated_AndTheResultOnlyOnce()
    {
        var s = await StartSittingAsync();
        using var _a = s.Admin;
        using var _c = s.Candidate;

        var early = await s.Admin.PostAsJsonAsync(Route(s, "invalidate"), new { reason = "Cheating" });
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);

        (await s.Candidate.PostAsync($"/v1/me/attempts/{s.AttemptId}/submit", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await s.Admin.PostAsJsonAsync(Route(s, "invalidate"), new { reason = "Cheating" })).StatusCode);
        var twice = await s.Admin.PostAsJsonAsync(Route(s, "invalidate"), new { reason = "Again" });
        Assert.Equal("attempt_already_invalidated", await ErrorCodeAsync(twice));
    }

    [Fact]
    public async Task TheStaffRow_ShowsDeparturesAndPausedState()
    {
        var s = await StartSittingAsync();
        using var _a = s.Admin;
        using var _c = s.Candidate;
        await s.Admin.PutAsJsonAsync($"/v1/exams/{s.ExamId}/focus-violation-limit", new { focusViolationLimit = 5 });
        (await s.Candidate.PostAsJsonAsync($"/v1/me/attempts/{s.AttemptId}/focus-violations", new { kind = "TabHidden" })).EnsureSuccessStatusCode();
        await s.Admin.PostAsync(Route(s, "pause"), null);

        var staff = await s.Admin.GetFromJsonAsync<JsonElement>($"/v1/exams/{s.ExamId}/attempts");
        var row = staff.GetProperty("candidates").EnumerateArray().SelectMany(c => c.GetProperty("attempts").EnumerateArray()).Single();

        Assert.Equal(1, row.GetProperty("focusViolations").GetInt32());
        Assert.True(row.GetProperty("paused").GetBoolean());
    }

    [Fact]
    public async Task AnAttemptOfAnotherExam_Is404()
    {
        var s = await StartSittingAsync();
        using var _a = s.Admin;
        using var _c = s.Candidate;

        var response = await s.Admin.PostAsync($"/v1/exams/{Guid.NewGuid()}/attempts/{s.AttemptId}/pause", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await StatusAsync(s)).GetProperty("pausedAtUtc").ValueKind);
    }

    [Fact]
    public async Task ACandidate_CannotUseAnyOfTheStaffActions()
    {
        var s = await StartSittingAsync();
        using var _a = s.Admin;
        using var _c = s.Candidate;

        foreach (var action in new[] { "warn", "pause", "resume", "terminate", "invalidate" })
            Assert.Equal(HttpStatusCode.Forbidden, (await s.Candidate.PostAsJsonAsync(Route(s, action), new { message = "x", reason = "x" })).StatusCode);

        Assert.Equal("InProgress", (await AttemptAsync(s)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task SomeoneElsesHeartbeat_Is404()
    {
        var s = await StartSittingAsync();
        using var _a = s.Admin;
        using var _c = s.Candidate;
        var (other, _) = await factory.EnrollNewCandidateAsync(s.Admin, s.ExamId);
        using var _o = other;

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/v1/me/attempts/{s.AttemptId}/status")).StatusCode);
    }

    [Fact]
    public async Task EveryAction_IsAudited_WithTheAdministratorWhoDidIt_AndWhy()
    {
        var s = await StartSittingAsync();
        using var _a = s.Admin;
        using var _c = s.Candidate;

        await s.Admin.PostAsJsonAsync(Route(s, "warn"), new { message = "Eyes on your own screen." });
        await s.Admin.PostAsync(Route(s, "pause"), null);
        await s.Admin.PostAsync(Route(s, "resume"), null);
        await s.Admin.PostAsJsonAsync(Route(s, "terminate"), new { reason = "Caught using a phone." });
        await s.Admin.PostAsJsonAsync(Route(s, "invalidate"), new { reason = "Result cannot be trusted." });

        using var scope = factory.Services.CreateScope();
        var entries = await scope.ServiceProvider.GetRequiredService<AdminDbContext>().AuditLogs
            .AsNoTracking()
            .Where(a => a.EntityId == s.AttemptId.ToString())
            .OrderBy(a => a.OccurredAtUtc)
            .ToListAsync();

        Assert.Equal(
            ["ExamRuntime.AttemptWarned", "ExamRuntime.AttemptPaused", "ExamRuntime.AttemptResumed", "ExamRuntime.AttemptTerminated", "ExamRuntime.AttemptInvalidated"],
            entries.Select(e => e.Action).ToArray());
        Assert.All(entries, e => Assert.NotNull(e.ActorUserId));
        Assert.All(entries, e => Assert.Equal("Attempt", e.EntityType));
        Assert.All(entries, e => Assert.Equal(s.ExamId.ToString(), e.Metadata["examId"]));
        Assert.Equal("Eyes on your own screen.", entries[0].Metadata["message"]);
        Assert.Equal("Caught using a phone.", entries[3].Metadata["reason"]);
        Assert.Equal("Result cannot be trusted.", entries[4].Metadata["reason"]);
    }
}
