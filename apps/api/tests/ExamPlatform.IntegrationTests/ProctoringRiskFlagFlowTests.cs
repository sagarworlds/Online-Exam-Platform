using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ExamPlatform.Modules.Identity.Domain.Rbac;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// The risk review (FR-27): a staff member scores an exam, reads the queue of flagged attempts with the signals behind each score, and
/// marks a flag reviewed or dismisses it with a note. Nothing here changes what the candidate sees: the attempt keeps its result.
/// </summary>
public sealed class ProctoringRiskFlagFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<Guid> PublishedExamAsync(HttpClient admin)
    {
        var question = await CreateQuestionAsync(admin, "Which is a prime number?", "7", "8");
        var examId = await CreateExamAsync(admin, $"Risk Exam {Guid.NewGuid():N}", [question], startsIn: TimeSpan.FromMinutes(-5));
        // A limit well above the departures these tests record, so no attempt is ended by the platform's own rule.
        (await admin.PutAsJsonAsync($"/v1/exams/{examId}/focus-violation-limit", new { focusViolationLimit = 10 })).EnsureSuccessStatusCode();
        return examId;
    }

    private static async Task<JsonElement> SitAndSubmitAsync(HttpClient candidate, Guid examId, int departures)
    {
        var started = await candidate.PostAsJsonAsync($"/v1/me/exams/{examId}/attempts", new { instructionsAcknowledged = true });
        started.EnsureSuccessStatusCode();
        var attemptId = (await started.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        for (var i = 0; i < departures; i++)
            (await candidate.PostAsJsonAsync($"/v1/me/attempts/{attemptId}/focus-violations", new { kind = "TabHidden" })).EnsureSuccessStatusCode();

        var attempt = await candidate.GetFromJsonAsync<JsonElement>($"/v1/me/attempts/{attemptId}");
        var question = attempt.GetProperty("sections")[0].GetProperty("questions")[0];
        var option = question.GetProperty("options")[0].GetProperty("id").GetGuid();
        (await candidate.PutAsJsonAsync($"/v1/me/attempts/{attemptId}/answers/{question.GetProperty("id").GetGuid()}", new { optionId = option }))
            .EnsureSuccessStatusCode();
        (await candidate.PostAsync($"/v1/me/attempts/{attemptId}/submit", content: null)).EnsureSuccessStatusCode();
        return attempt;
    }

    private static async Task<JsonElement> QueueAsync(HttpClient staff, Guid examId, string filter = "open") =>
        await staff.GetFromJsonAsync<JsonElement>($"/v1/proctoring/exams/{examId}/risk-flags?filter={filter}");

    private static async Task<Guid> OnlyFlagIdAsync(HttpClient staff, Guid examId, string filter = "open") =>
        (await QueueAsync(staff, examId, filter)).GetProperty("items")[0].GetProperty("id").GetGuid();

    private static HttpClient ClientAs(ApiFactory factory, SignedInTestUser caller)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", caller.AccessToken);
        return client;
    }

    [Fact]
    public async Task AScan_FlagsAnAttemptWithThreeDepartures_AndTheQueueShowsTheSignalsBehindIt()
    {
        using var admin = await factory.AdminClientAsync();
        var examId = await PublishedExamAsync(admin);
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;
        await SitAndSubmitAsync(candidate, examId, departures: 3);

        var scan = await admin.PostAsync($"/v1/proctoring/exams/{examId}/risk-scan", content: null);

        Assert.Equal(HttpStatusCode.OK, scan.StatusCode);
        var summary = await scan.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, summary.GetProperty("scored").GetInt32());
        Assert.Equal(1, summary.GetProperty("flagged").GetInt32());
        Assert.Equal(0, summary.GetProperty("keptDecided").GetInt32());

        var queue = await QueueAsync(admin, examId);
        Assert.Equal(1, queue.GetProperty("total").GetInt32());
        Assert.Equal("Open", queue.GetProperty("filter").GetString());
        var flag = queue.GetProperty("items")[0];
        Assert.Equal(30, flag.GetProperty("score").GetInt32());
        Assert.Equal(100, flag.GetProperty("maxScore").GetInt32());
        Assert.Equal("Open", flag.GetProperty("status").GetString());

        // Every signal is listed, raised or not, with the value read, the threshold and the weight, so a reviewer can check the sum.
        var signals = flag.GetProperty("signals").EnumerateArray().ToDictionary(s => s.GetProperty("kind").GetString()!, s => s);
        Assert.Equal(5, signals.Count);
        Assert.True(signals["FocusDepartures"].GetProperty("raised").GetBoolean());
        Assert.Equal(3m, signals["FocusDepartures"].GetProperty("value").GetDecimal());
        Assert.Equal(30, signals["FocusDepartures"].GetProperty("points").GetInt32());
        Assert.False(signals["Invalidated"].GetProperty("raised").GetBoolean());
    }

    [Fact]
    public async Task AScan_LeavesAnAttemptWithNoSignalsUnflagged()
    {
        using var admin = await factory.AdminClientAsync();
        var examId = await PublishedExamAsync(admin);
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;
        await SitAndSubmitAsync(candidate, examId, departures: 0);

        var scan = await (await admin.PostAsync($"/v1/proctoring/exams/{examId}/risk-scan", content: null)).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(1, scan.GetProperty("scored").GetInt32());
        Assert.Equal(0, scan.GetProperty("flagged").GetInt32());
        Assert.Equal(0, (await QueueAsync(admin, examId)).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task AReviewer_MarksAFlagReviewed_AndItLeavesTheOpenQueue()
    {
        using var admin = await factory.AdminClientAsync();
        var examId = await PublishedExamAsync(admin);
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;
        await SitAndSubmitAsync(candidate, examId, departures: 3);
        await admin.PostAsync($"/v1/proctoring/exams/{examId}/risk-scan", content: null);
        var flagId = await OnlyFlagIdAsync(admin, examId);

        var reviewed = await admin.PostAsJsonAsync($"/v1/proctoring/risk-flags/{flagId}/review", new { note = "Checked; nothing further" });

        Assert.Equal(HttpStatusCode.NoContent, reviewed.StatusCode);
        Assert.Equal(0, (await QueueAsync(admin, examId, "open")).GetProperty("total").GetInt32());
        var done = await QueueAsync(admin, examId, "reviewed");
        Assert.Equal(1, done.GetProperty("total").GetInt32());
        Assert.Equal("Checked; nothing further", done.GetProperty("items")[0].GetProperty("decisionNote").GetString());
    }

    [Fact]
    public async Task ADismissal_WithoutANote_IsA400_AndTheFlagStaysOpen()
    {
        using var admin = await factory.AdminClientAsync();
        var examId = await PublishedExamAsync(admin);
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;
        await SitAndSubmitAsync(candidate, examId, departures: 3);
        await admin.PostAsync($"/v1/proctoring/exams/{examId}/risk-scan", content: null);
        var flagId = await OnlyFlagIdAsync(admin, examId);

        var blank = await admin.PostAsJsonAsync($"/v1/proctoring/risk-flags/{flagId}/dismiss", new { note = "   " });
        var missing = await admin.PostAsJsonAsync($"/v1/proctoring/risk-flags/{flagId}/dismiss", new { });

        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(1, (await QueueAsync(admin, examId, "open")).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task ADismissal_WithANote_IsRecorded_AndASecondScanKeepsIt()
    {
        using var admin = await factory.AdminClientAsync();
        var examId = await PublishedExamAsync(admin);
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;
        await SitAndSubmitAsync(candidate, examId, departures: 3);
        await admin.PostAsync($"/v1/proctoring/exams/{examId}/risk-scan", content: null);
        var flagId = await OnlyFlagIdAsync(admin, examId);

        var dismissed = await admin.PostAsJsonAsync($"/v1/proctoring/risk-flags/{flagId}/dismiss", new { note = "Power cut; the centre confirmed it" });
        var rescan = await (await admin.PostAsync($"/v1/proctoring/exams/{examId}/risk-scan", content: null)).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.NoContent, dismissed.StatusCode);
        Assert.Equal(1, rescan.GetProperty("keptDecided").GetInt32());
        Assert.Equal(0, rescan.GetProperty("scored").GetInt32());
        var decided = await QueueAsync(admin, examId, "dismissed");
        Assert.Equal(1, decided.GetProperty("total").GetInt32());
        Assert.Equal("Power cut; the centre confirmed it", decided.GetProperty("items")[0].GetProperty("decisionNote").GetString());
    }

    [Fact]
    public async Task ADecidedFlag_CannotBeDecidedAgain_AndIsA409()
    {
        using var admin = await factory.AdminClientAsync();
        var examId = await PublishedExamAsync(admin);
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;
        await SitAndSubmitAsync(candidate, examId, departures: 3);
        await admin.PostAsync($"/v1/proctoring/exams/{examId}/risk-scan", content: null);
        var flagId = await OnlyFlagIdAsync(admin, examId);
        await admin.PostAsJsonAsync($"/v1/proctoring/risk-flags/{flagId}/review", new { });

        var again = await admin.PostAsJsonAsync($"/v1/proctoring/risk-flags/{flagId}/dismiss", new { note = "Changed my mind" });

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task AnUnknownFilter_IsA400()
    {
        using var admin = await factory.AdminClientAsync();
        var examId = await PublishedExamAsync(admin);

        var response = await admin.GetAsync($"/v1/proctoring/exams/{examId}/risk-flags?filter=closed");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AnUnknownExam_IsA404_ForAScanAndForTheQueue()
    {
        using var admin = await factory.AdminClientAsync();
        var unknown = Guid.NewGuid();

        var scan = await admin.PostAsync($"/v1/proctoring/exams/{unknown}/risk-scan", content: null);
        var queue = await admin.GetAsync($"/v1/proctoring/exams/{unknown}/risk-flags");

        Assert.Equal(HttpStatusCode.NotFound, scan.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, queue.StatusCode);
    }

    [Fact]
    public async Task AProctor_CanRunTheReview()
    {
        using var admin = await factory.AdminClientAsync();
        var examId = await PublishedExamAsync(admin);
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;
        await SitAndSubmitAsync(candidate, examId, departures: 3);
        using var proctor = ClientAs(factory, await factory.SignInAsAsync(RbacCatalog.RoleNames.Proctor));

        var scan = await proctor.PostAsync($"/v1/proctoring/exams/{examId}/risk-scan", content: null);
        var queue = await proctor.GetAsync($"/v1/proctoring/exams/{examId}/risk-flags");

        Assert.Equal(HttpStatusCode.OK, scan.StatusCode);
        Assert.Equal(HttpStatusCode.OK, queue.StatusCode);
    }
}
