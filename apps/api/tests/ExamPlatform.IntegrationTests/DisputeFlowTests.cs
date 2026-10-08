using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// Disputing an answer key (FR-31) over real HTTP and a real database: a candidate who can see the key of their released result says
/// it is wrong, staff either correct it (which rescores the result and settles every dispute of that question) or leave it and say why,
/// and the candidate sees the answer under their result.
/// </summary>
public sealed class DisputeFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await JsonAsync(response)).GetProperty("title").GetString());
    }

    /// <summary>One question whose key is wrong (the bank marks "Rome", the first option, correct), in a published exam with a candidate invited.</summary>
    private sealed record Setup(HttpClient Admin, HttpClient Candidate, Guid ExamId, Guid QuestionId, string ExamName);

    private async Task<Setup> EnrolledAsync()
    {
        var admin = await factory.AdminClientAsync();
        var questionId = await CreateQuestionAsync(admin, "Capital of France?", "Rome", "Paris");
        var name = $"Geography {Guid.NewGuid():N}";
        var examId = await CreateExamAsync(admin, name, [questionId], TimeSpan.FromMinutes(-5));
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        return new Setup(admin, candidate, examId, questionId, name);
    }

    private static async Task<Guid> StartAsync(HttpClient candidate, Guid examId) =>
        (await JsonAsync((await candidate.PostAsJsonAsync($"/v1/me/exams/{examId}/attempts", new { instructionsAcknowledged = true })).EnsureSuccessStatusCode())).GetProperty("id").GetGuid();

    /// <summary>Starts an attempt, chooses "Paris" (which the wrong key marks wrong) and submits; returns the attempt and the id of "Paris".</summary>
    private static async Task<(Guid AttemptId, Guid Paris)> SitAsync(HttpClient candidate, Guid examId)
    {
        var attemptId = await StartAsync(candidate, examId);
        var attempt = await candidate.GetFromJsonAsync<JsonElement>($"/v1/me/attempts/{attemptId}");
        var question = attempt.GetProperty("sections")[0].GetProperty("questions")[0];
        var paris = question.GetProperty("options").EnumerateArray().Single(o => o.GetProperty("text").GetString() == "Paris").GetProperty("id").GetGuid();
        (await candidate.PutAsJsonAsync($"/v1/me/attempts/{attemptId}/answers/{question.GetProperty("id").GetGuid()}", new { optionId = paris })).EnsureSuccessStatusCode();
        var submitted = await JsonAsync((await candidate.PostAsync($"/v1/me/attempts/{attemptId}/submit", content: null)).EnsureSuccessStatusCode());
        Assert.Equal(0m, submitted.GetProperty("score").GetDecimal());
        return (attemptId, paris);
    }

    private static Task<HttpResponseMessage> DisputeAsync(HttpClient candidate, Guid attemptId, Guid questionId, string? reason = "Paris is the capital of France") =>
        candidate.PostAsJsonAsync($"/v1/me/attempts/{attemptId}/disputes", new { questionId, reason });

    private static async Task<JsonElement> ReviewAsync(HttpClient candidate, Guid attemptId) =>
        await JsonAsync((await candidate.GetAsync($"/v1/me/attempts/{attemptId}/review")).EnsureSuccessStatusCode());

    private static async Task<JsonElement?> QueueEntryAsync(HttpClient admin, Guid disputeId, string? status = null)
    {
        var listed = await JsonAsync((await admin.GetAsync(status is null ? "/v1/disputes" : $"/v1/disputes?status={status}")).EnsureSuccessStatusCode());
        foreach (var entry in listed.EnumerateArray())
            if (entry.GetProperty("id").GetGuid() == disputeId)
                return entry;
        return null;
    }

    [Fact]
    public async Task ADisputedKey_WhenStaffCorrectIt_IsAcceptedAndTheResultIsRescoredAndVersioned()
    {
        var s = await EnrolledAsync();
        using var _a = s.Admin;
        using var _c = s.Candidate;
        var (attemptId, paris) = await SitAsync(s.Candidate, s.ExamId);

        var created = await DisputeAsync(s.Candidate, attemptId, s.QuestionId, "  Paris is the capital of France  ");

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var raised = await JsonAsync(created);
        var disputeId = raised.GetProperty("id").GetGuid();
        Assert.Equal("Open", raised.GetProperty("status").GetString());
        Assert.Equal("Paris is the capital of France", raised.GetProperty("reason").GetString());

        // The candidate's result shows it, and that the window is open.
        var before = await ReviewAsync(s.Candidate, attemptId);
        Assert.Equal(1, before.GetProperty("resultVersion").GetInt32());
        Assert.True(before.GetProperty("disputeWindow").GetProperty("open").GetBoolean());
        Assert.Equal(disputeId, Assert.Single(before.GetProperty("disputes").EnumerateArray()).GetProperty("id").GetGuid());

        // Staff see it in the queue, with who, which exam, which question.
        var queued = (await QueueEntryAsync(s.Admin, disputeId))!.Value;
        Assert.Equal(s.ExamName, queued.GetProperty("examName").GetString());
        Assert.Equal(1, queued.GetProperty("attemptNumber").GetInt32());
        Assert.Equal(s.QuestionId, queued.GetProperty("questionId").GetGuid());
        Assert.Contains("Capital of France?", queued.GetProperty("questionText").GetString());
        Assert.Equal("Paris is the capital of France", queued.GetProperty("reason").GetString());
        Assert.Contains("@tests.local", queued.GetProperty("candidateEmail").GetString());

        // Correcting the key is what accepts it.
        (await s.Admin.PostAsJsonAsync($"/v1/questions/{s.QuestionId}/correct-answer-key",
            new { correctOptionIds = new[] { paris }, reason = "Paris is the capital of France, not Rome" })).EnsureSuccessStatusCode();

        var after = await ReviewAsync(s.Candidate, attemptId);
        Assert.Equal(1m, after.GetProperty("score").GetDecimal());
        Assert.Equal(2, after.GetProperty("resultVersion").GetInt32());
        Assert.Equal(2, Assert.Single(after.GetProperty("revisions").EnumerateArray()).GetProperty("version").GetInt32());
        var settled = Assert.Single(after.GetProperty("disputes").EnumerateArray());
        Assert.Equal("Accepted", settled.GetProperty("status").GetString());
        Assert.Equal("Paris is the capital of France, not Rome", settled.GetProperty("resolutionNote").GetString());

        // It leaves the open queue and is found among the accepted ones.
        Assert.Null(await QueueEntryAsync(s.Admin, disputeId));
        Assert.NotNull(await QueueEntryAsync(s.Admin, disputeId, "accepted"));
    }

    [Fact]
    public async Task ACorrection_SettlesEveryCandidatesDisputeOfThatQuestion()
    {
        var s = await EnrolledAsync();
        using var _a = s.Admin;
        using var _c = s.Candidate;
        var (second, _) = await factory.EnrollNewCandidateAsync(s.Admin, s.ExamId);
        using var _s = second;
        var (firstAttempt, paris) = await SitAsync(s.Candidate, s.ExamId);
        var (secondAttempt, _) = await SitAsync(second, s.ExamId);
        var one = (await JsonAsync((await DisputeAsync(s.Candidate, firstAttempt, s.QuestionId)).EnsureSuccessStatusCode())).GetProperty("id").GetGuid();
        var two = (await JsonAsync((await DisputeAsync(second, secondAttempt, s.QuestionId, "The key is wrong")).EnsureSuccessStatusCode())).GetProperty("id").GetGuid();

        (await s.Admin.PostAsJsonAsync($"/v1/questions/{s.QuestionId}/correct-answer-key",
            new { correctOptionIds = new[] { paris }, reason = "Wrong key" })).EnsureSuccessStatusCode();

        Assert.NotNull(await QueueEntryAsync(s.Admin, one, "accepted"));
        Assert.NotNull(await QueueEntryAsync(s.Admin, two, "accepted"));
    }

    [Fact]
    public async Task ARejectedDispute_TellsTheCandidateWhy_AndIsFinal()
    {
        var s = await EnrolledAsync();
        using var _a = s.Admin;
        using var _c = s.Candidate;
        var (attemptId, _) = await SitAsync(s.Candidate, s.ExamId);
        var disputeId = (await JsonAsync((await DisputeAsync(s.Candidate, attemptId, s.QuestionId)).EnsureSuccessStatusCode())).GetProperty("id").GetGuid();

        var rejected = await JsonAsync((await s.Admin.PostAsJsonAsync($"/v1/disputes/{disputeId}/reject", new { note = "  The key is checked and stands.  " })).EnsureSuccessStatusCode());

        Assert.Equal("Rejected", rejected.GetProperty("status").GetString());
        Assert.Equal("The key is checked and stands.", rejected.GetProperty("resolutionNote").GetString());
        var seen = Assert.Single((await ReviewAsync(s.Candidate, attemptId)).GetProperty("disputes").EnumerateArray());
        Assert.Equal("Rejected", seen.GetProperty("status").GetString());
        Assert.Equal("The key is checked and stands.", seen.GetProperty("resolutionNote").GetString());
        // The result did not move.
        Assert.Equal(0m, (await ReviewAsync(s.Candidate, attemptId)).GetProperty("score").GetDecimal());

        // Settled: not again by staff, and not queued again by the candidate.
        await AssertProblemAsync(await s.Admin.PostAsJsonAsync($"/v1/disputes/{disputeId}/reject", new { note = "Again" }), HttpStatusCode.Conflict, "dispute_not_open");
        await AssertProblemAsync(await DisputeAsync(s.Candidate, attemptId, s.QuestionId), HttpStatusCode.Conflict, "dispute_already_raised");
    }

    [Fact]
    public async Task ACandidate_DisputesAQuestionOfAnAttemptOnce_EvenWhileItIsStillOpen()
    {
        var s = await EnrolledAsync();
        using var _a = s.Admin;
        using var _c = s.Candidate;
        var (attemptId, _) = await SitAsync(s.Candidate, s.ExamId);
        (await DisputeAsync(s.Candidate, attemptId, s.QuestionId)).EnsureSuccessStatusCode();

        await AssertProblemAsync(await DisputeAsync(s.Candidate, attemptId, s.QuestionId, "Again"), HttpStatusCode.Conflict, "dispute_already_raised");
    }

    [Fact]
    public async Task ADisputeNeedsAReason_AndAQuestionOfTheAttempt()
    {
        var s = await EnrolledAsync();
        using var _a = s.Admin;
        using var _c = s.Candidate;
        var (attemptId, _) = await SitAsync(s.Candidate, s.ExamId);

        await AssertProblemAsync(await DisputeAsync(s.Candidate, attemptId, s.QuestionId, "  "), HttpStatusCode.BadRequest, "invalid_attempt");
        await AssertProblemAsync(await DisputeAsync(s.Candidate, attemptId, s.QuestionId, new string('x', 1001)), HttpStatusCode.BadRequest, "invalid_attempt");
        await AssertProblemAsync(await DisputeAsync(s.Candidate, attemptId, Guid.NewGuid()), HttpStatusCode.NotFound, "question_not_in_attempt");
    }

    [Fact]
    public async Task OnlyTheCandidateWhoSatTheAttempt_CanDisputeIt()
    {
        var s = await EnrolledAsync();
        using var _a = s.Admin;
        using var _c = s.Candidate;
        var (attemptId, _) = await SitAsync(s.Candidate, s.ExamId);
        var (other, _) = await factory.EnrollNewCandidateAsync(s.Admin, s.ExamId);
        using var _o = other;

        await AssertProblemAsync(await DisputeAsync(other, attemptId, s.QuestionId), HttpStatusCode.NotFound, "attempt_not_found");
    }

    [Fact]
    public async Task AnAttemptStillInProgress_CannotBeDisputed()
    {
        var s = await EnrolledAsync();
        using var _a = s.Admin;
        using var _c = s.Candidate;
        var attemptId = await StartAsync(s.Candidate, s.ExamId);

        await AssertProblemAsync(await DisputeAsync(s.Candidate, attemptId, s.QuestionId), HttpStatusCode.Conflict, "attempt_not_submitted");
    }

    [Fact]
    public async Task AKey_CannotBeDisputedUntilTheAnswersAreReleased_ThenCan()
    {
        var s = await EnrolledAsync();
        using var _a = s.Admin;
        using var _c = s.Candidate;
        (await s.Admin.PutAsJsonAsync($"/v1/exams/{s.ExamId}/result-release", new { mode = "Manual" })).EnsureSuccessStatusCode();
        var (attemptId, _) = await SitAsync(s.Candidate, s.ExamId);

        await AssertProblemAsync(await DisputeAsync(s.Candidate, attemptId, s.QuestionId), HttpStatusCode.Conflict, "results_not_released");

        (await s.Admin.PostAsync($"/v1/exams/{s.ExamId}/results/release", content: null)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Created, (await DisputeAsync(s.Candidate, attemptId, s.QuestionId)).StatusCode);
    }

    [Fact]
    public async Task TheQueue_RefusesAStatusItDoesNotKnow_AndAnUnknownDisputeIsNotFound()
    {
        using var admin = await factory.AdminClientAsync();

        await AssertProblemAsync(await admin.GetAsync("/v1/disputes?status=maybe"), HttpStatusCode.BadRequest, "invalid_attempt");
        await AssertProblemAsync(await admin.PostAsJsonAsync($"/v1/disputes/{Guid.NewGuid()}/reject", new { note = "No" }), HttpStatusCode.NotFound, "dispute_not_found");
    }

    [Fact]
    public async Task RejectingWithoutAnExplanation_IsRefused_AndLeavesTheDisputeOpen()
    {
        var s = await EnrolledAsync();
        using var _a = s.Admin;
        using var _c = s.Candidate;
        var (attemptId, _) = await SitAsync(s.Candidate, s.ExamId);
        var disputeId = (await JsonAsync((await DisputeAsync(s.Candidate, attemptId, s.QuestionId)).EnsureSuccessStatusCode())).GetProperty("id").GetGuid();

        await AssertProblemAsync(await s.Admin.PostAsJsonAsync($"/v1/disputes/{disputeId}/reject", new { note = " " }), HttpStatusCode.BadRequest, "invalid_attempt");

        Assert.NotNull(await QueueEntryAsync(s.Admin, disputeId));
    }

    [Fact]
    public async Task ACandidate_CannotUseTheStaffQueue()
    {
        var (candidate, _) = await factory.CandidateClientAsync();
        using var _c = candidate;

        Assert.Equal(HttpStatusCode.Forbidden, (await candidate.GetAsync("/v1/disputes")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await candidate.PostAsJsonAsync($"/v1/disputes/{Guid.NewGuid()}/reject", new { note = "x" })).StatusCode);
    }
}
