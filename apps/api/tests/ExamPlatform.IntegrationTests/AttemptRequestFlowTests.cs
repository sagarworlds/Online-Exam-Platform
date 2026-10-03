using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// A candidate asking for another attempt, and an administrator answering, over real HTTP and a real database: the request waits in a
/// queue, approving it gives the attempt, declining it tells the candidate why.
/// </summary>
public sealed class AttemptRequestFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await JsonAsync(response)).GetProperty("title").GetString());
    }

    private async Task<(HttpClient Admin, HttpClient Candidate, Guid CandidateId, Guid ExamId)> EnrolledAsync()
    {
        var admin = await factory.AdminClientAsync();
        var question = await CreateQuestionAsync(admin, "Request question", "Right", "Wrong");
        var examId = await CreateExamAsync(admin, "Attempt request exam", [question], TimeSpan.FromMinutes(-5));
        var (candidate, user) = await factory.EnrollNewCandidateAsync(admin, examId);
        return (admin, candidate, user.UserId, examId);
    }

    /// <summary>Starts the attempt, answers nothing, and submits it, so the candidate has used the attempt they hold.</summary>
    private static async Task UseTheAttemptAsync(HttpClient candidate, Guid examId)
    {
        var attempt = await JsonAsync((await candidate.PostAsync($"/v1/me/exams/{examId}/attempts", content: null)).EnsureSuccessStatusCode());
        (await candidate.PostAsync($"/v1/me/attempts/{attempt.GetProperty("id").GetGuid()}/submit", content: null)).EnsureSuccessStatusCode();
    }

    private static Task<HttpResponseMessage> AskAsync(HttpClient candidate, Guid examId, string? message = null) =>
        candidate.PostAsJsonAsync($"/v1/me/exams/{examId}/attempt-requests", new { message });

    private static async Task<JsonElement> MyExamAsync(HttpClient candidate, Guid examId) =>
        (await candidate.GetFromJsonAsync<JsonElement>("/v1/me/exams")).EnumerateArray().Single(e => e.GetProperty("examId").GetGuid() == examId);

    private static async Task<JsonElement?> QueuedAsync(HttpClient admin, Guid examId, string status = "pending")
    {
        var list = (await admin.GetFromJsonAsync<JsonElement>($"/v1/attempt-requests?status={status}")).EnumerateArray()
            .Where(r => r.GetProperty("examId").GetGuid() == examId).ToList();
        return list.Count == 0 ? null : list.Single();
    }

    [Fact]
    public async Task ARequest_WaitsInTheQueue_AndApprovingItGivesTheCandidateAnotherAttempt()
    {
        var (admin, candidate, candidateId, examId) = await EnrolledAsync();
        using var _a = admin;
        using var _c = candidate;
        await UseTheAttemptAsync(candidate, examId);
        Assert.True((await MyExamAsync(candidate, examId)).GetProperty("canRequestAttempt").GetBoolean());

        var asked = await AskAsync(candidate, examId, "  Power cut  ");

        Assert.Equal(HttpStatusCode.Created, asked.StatusCode);
        var mine = await MyExamAsync(candidate, examId);
        Assert.False(mine.GetProperty("canRequestAttempt").GetBoolean());
        Assert.Equal("Pending", mine.GetProperty("attemptRequest").GetProperty("status").GetString());

        var queued = (await QueuedAsync(admin, examId))!.Value;
        Assert.Equal("Power cut", queued.GetProperty("message").GetString());
        Assert.Equal(candidateId, queued.GetProperty("candidateId").GetGuid());
        Assert.Equal("Attempt request exam", queued.GetProperty("examName").GetString());

        var approved = await JsonAsync((await admin.PostAsync($"/v1/attempt-requests/{queued.GetProperty("id").GetGuid()}/approve", content: null)).EnsureSuccessStatusCode());

        Assert.Equal("Approved", approved.GetProperty("status").GetString());
        Assert.Null(await QueuedAsync(admin, examId));
        var after = await MyExamAsync(candidate, examId);
        Assert.Equal(2, after.GetProperty("attemptsAllowed").GetInt32());
        Assert.True(after.GetProperty("canStartAttempt").GetBoolean());
        Assert.Equal("Approved", after.GetProperty("attemptRequest").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Declining_TellsTheCandidateWhy_AndTheyMayAskAgain()
    {
        var (admin, candidate, _, examId) = await EnrolledAsync();
        using var _a = admin;
        using var _c = candidate;
        await UseTheAttemptAsync(candidate, examId);
        (await AskAsync(candidate, examId)).EnsureSuccessStatusCode();
        var id = (await QueuedAsync(admin, examId))!.Value.GetProperty("id").GetGuid();

        var declined = await JsonAsync((await admin.PostAsJsonAsync($"/v1/attempt-requests/{id}/decline", new { note = "Speak to your teacher" })).EnsureSuccessStatusCode());

        Assert.Equal("Declined", declined.GetProperty("status").GetString());
        var mine = await MyExamAsync(candidate, examId);
        Assert.Equal("Speak to your teacher", mine.GetProperty("attemptRequest").GetProperty("decisionNote").GetString());
        Assert.False(mine.GetProperty("canStartAttempt").GetBoolean());
        Assert.True(mine.GetProperty("canRequestAttempt").GetBoolean());
        (await AskAsync(candidate, examId, "Please reconsider")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task ARequest_CannotBeMadeBeforeTheAttemptIsUsed_OrTwiceAtOnce()
    {
        var (admin, candidate, _, examId) = await EnrolledAsync();
        using var _a = admin;
        using var _c = candidate;

        await AssertProblemAsync(await AskAsync(candidate, examId), HttpStatusCode.Conflict, "attempt_not_needed");

        await UseTheAttemptAsync(candidate, examId);
        (await AskAsync(candidate, examId)).EnsureSuccessStatusCode();
        await AssertProblemAsync(await AskAsync(candidate, examId), HttpStatusCode.Conflict, "attempt_request_pending");
    }

    [Fact]
    public async Task ADecidedRequest_CannotBeDecidedAgain()
    {
        var (admin, candidate, _, examId) = await EnrolledAsync();
        using var _a = admin;
        using var _c = candidate;
        await UseTheAttemptAsync(candidate, examId);
        (await AskAsync(candidate, examId)).EnsureSuccessStatusCode();
        var id = (await QueuedAsync(admin, examId))!.Value.GetProperty("id").GetGuid();
        (await admin.PostAsync($"/v1/attempt-requests/{id}/approve", content: null)).EnsureSuccessStatusCode();

        await AssertProblemAsync(await admin.PostAsync($"/v1/attempt-requests/{id}/approve", content: null), HttpStatusCode.Conflict, "attempt_request_not_pending");
        await AssertProblemAsync(await admin.PostAsJsonAsync($"/v1/attempt-requests/{id}/decline", new { note = "late" }), HttpStatusCode.Conflict, "attempt_request_not_pending");
        Assert.NotNull(await QueuedAsync(admin, examId, "approved"));
    }

    [Fact]
    public async Task AnUnknownRequest_Is404_ForBothAnswers()
    {
        using var admin = await factory.AdminClientAsync();

        await AssertProblemAsync(await admin.PostAsync($"/v1/attempt-requests/{Guid.NewGuid()}/approve", content: null), HttpStatusCode.NotFound, "attempt_request_not_found");
        await AssertProblemAsync(await admin.PostAsJsonAsync($"/v1/attempt-requests/{Guid.NewGuid()}/decline", new { note = "x" }), HttpStatusCode.NotFound, "attempt_request_not_found");
    }

    [Fact]
    public async Task ACandidate_CannotReadTheQueue_OrAnswerARequest()
    {
        var (admin, candidate, _, examId) = await EnrolledAsync();
        using var _a = admin;
        using var _c = candidate;
        await UseTheAttemptAsync(candidate, examId);
        (await AskAsync(candidate, examId)).EnsureSuccessStatusCode();
        var id = (await QueuedAsync(admin, examId))!.Value.GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.Forbidden, (await candidate.GetAsync("/v1/attempt-requests")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await candidate.PostAsync($"/v1/attempt-requests/{id}/approve", content: null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await candidate.PostAsJsonAsync($"/v1/attempt-requests/{id}/decline", new { note = "x" })).StatusCode);
    }

    [Fact]
    public async Task ARequestForAnExamTheCandidateIsNotEnrolledIn_Is404()
    {
        var (admin, _, _, examId) = await EnrolledAsync();
        using var _a = admin;
        var (stranger, _) = await factory.EnrollNewCandidateAsync(admin, (await EnrolledAsync()).ExamId);
        using var _s = stranger;

        await AssertProblemAsync(await AskAsync(stranger, examId), HttpStatusCode.NotFound, "exam_not_available");
    }

    [Fact]
    public async Task AStatusThatIsNotOneOfOurs_Is400()
    {
        using var admin = await factory.AdminClientAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/v1/attempt-requests?status=maybe")).StatusCode);
    }
}
