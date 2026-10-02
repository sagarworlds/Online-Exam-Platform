using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// Extra attempts over real HTTP and a real database: everyone has one attempt, an administrator can give a candidate another
/// once they have used what they hold, and the attempts stay separate, numbered, scored and reviewable one by one.
/// </summary>
public sealed class ExtraAttemptFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await JsonAsync(response)).GetProperty("title").GetString());
    }

    /// <summary>A published exam of one question, an administrator, and a candidate who accepted the invitation.</summary>
    private async Task<(HttpClient Admin, HttpClient Candidate, Guid CandidateId, Guid ExamId)> EnrolledAsync()
    {
        var admin = await factory.AdminClientAsync();
        var question = await CreateQuestionAsync(admin, "Extra attempt question", "Right", "Wrong");
        var examId = await CreateExamAsync(admin, "Extra attempts exam", [question], TimeSpan.FromMinutes(-5));
        var (candidate, user) = await factory.EnrollNewCandidateAsync(admin, examId);
        return (admin, candidate, user.UserId, examId);
    }

    private static async Task<JsonElement> StartAsync(HttpClient candidate, Guid examId)
    {
        var response = await candidate.PostAsync($"/v1/me/exams/{examId}/attempts", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await JsonAsync(response);
    }

    /// <summary>Starts (or resumes) an attempt, answers the question as asked, submits, and returns the submitted attempt.</summary>
    private static async Task<JsonElement> SitAsync(HttpClient candidate, Guid examId, string choose)
    {
        var attempt = await StartAsync(candidate, examId);
        var attemptId = attempt.GetProperty("id").GetGuid();
        var question = attempt.GetProperty("sections")[0].GetProperty("questions")[0];
        var option = question.GetProperty("options").EnumerateArray().Single(o => o.GetProperty("text").GetString() == choose).GetProperty("id").GetGuid();
        (await candidate.PutAsJsonAsync($"/v1/me/attempts/{attemptId}/answers/{question.GetProperty("id").GetGuid()}", new { optionId = option })).EnsureSuccessStatusCode();
        return await JsonAsync((await candidate.PostAsync($"/v1/me/attempts/{attemptId}/submit", content: null)).EnsureSuccessStatusCode());
    }

    private static Task<HttpResponseMessage> GrantAsync(HttpClient admin, Guid examId, Guid candidateId, string? reason = null) =>
        admin.PostAsJsonAsync($"/v1/exams/{examId}/candidates/{candidateId}/extra-attempts", new { reason });

    private static async Task<JsonElement> MyExamAsync(HttpClient candidate, Guid examId) =>
        (await candidate.GetFromJsonAsync<JsonElement>("/v1/me/exams")).EnumerateArray().Single(e => e.GetProperty("examId").GetGuid() == examId);

    private static async Task<JsonElement> StaffRowAsync(HttpClient admin, Guid examId, Guid candidateId) =>
        (await admin.GetFromJsonAsync<JsonElement>($"/v1/exams/{examId}/attempts")).GetProperty("candidates").EnumerateArray()
            .Single(c => c.GetProperty("candidateId").GetGuid() == candidateId);

    [Fact]
    public async Task ACandidate_HasOneAttempt_AndStartingAgainOnlyShowsItsResult()
    {
        var (admin, candidate, _, examId) = await EnrolledAsync();
        using var _a = admin;
        using var _c = candidate;
        var first = await SitAsync(candidate, examId, "Right");

        var again = await StartAsync(candidate, examId);

        Assert.Equal(first.GetProperty("id").GetGuid(), again.GetProperty("id").GetGuid());
        Assert.Equal("Submitted", again.GetProperty("status").GetString());
        Assert.Equal(1, again.GetProperty("number").GetInt32());
        var mine = await MyExamAsync(candidate, examId);
        Assert.Equal(1, mine.GetProperty("attemptsAllowed").GetInt32());
        Assert.Equal(1, mine.GetProperty("attemptsUsed").GetInt32());
        Assert.False(mine.GetProperty("canStartAttempt").GetBoolean());
    }

    [Fact]
    public async Task AnExtraAttempt_LetsTheCandidateSitAgain_WithItsOwnScore_AndEachAttemptKeepsItsOwnReview()
    {
        var (admin, candidate, candidateId, examId) = await EnrolledAsync();
        using var _a = admin;
        using var _c = candidate;
        var first = await SitAsync(candidate, examId, "Wrong");
        Assert.Equal(0m, first.GetProperty("score").GetDecimal());

        var granted = await GrantAsync(admin, examId, candidateId, "  Power cut  ");

        Assert.Equal(HttpStatusCode.Created, granted.StatusCode);
        var row = await JsonAsync(granted);
        Assert.Equal(2, row.GetProperty("attemptsAllowed").GetInt32());
        Assert.False(row.GetProperty("canGrant").GetBoolean());
        Assert.True((await MyExamAsync(candidate, examId)).GetProperty("canStartAttempt").GetBoolean());

        var second = await SitAsync(candidate, examId, "Right");

        Assert.Equal(2, second.GetProperty("number").GetInt32());
        Assert.NotEqual(first.GetProperty("id").GetGuid(), second.GetProperty("id").GetGuid());
        Assert.Equal(1m, second.GetProperty("score").GetDecimal());

        var mine = await MyExamAsync(candidate, examId);
        Assert.Equal([1, 2], mine.GetProperty("attempts").EnumerateArray().Select(a => a.GetProperty("number").GetInt32()));
        Assert.Equal([0m, 1m], mine.GetProperty("attempts").EnumerateArray().Select(a => a.GetProperty("score").GetDecimal()));
        Assert.Equal(second.GetProperty("id").GetGuid(), mine.GetProperty("attemptId").GetGuid());
        Assert.False(mine.GetProperty("canStartAttempt").GetBoolean());

        // Each attempt is reviewed on its own: the first still shows the wrong answer it gave.
        var firstReview = await candidate.GetFromJsonAsync<JsonElement>($"/v1/me/attempts/{first.GetProperty("id").GetGuid()}/review");
        var secondReview = await candidate.GetFromJsonAsync<JsonElement>($"/v1/me/attempts/{second.GetProperty("id").GetGuid()}/review");
        Assert.Equal((0, 1), (firstReview.GetProperty("correctCount").GetInt32(), firstReview.GetProperty("wrongCount").GetInt32()));
        Assert.Equal((1, 0), (secondReview.GetProperty("correctCount").GetInt32(), secondReview.GetProperty("wrongCount").GetInt32()));
        Assert.Equal(1, firstReview.GetProperty("number").GetInt32());
        Assert.Equal(2, secondReview.GetProperty("number").GetInt32());

        // And starting again, with nothing left, only shows the latest result.
        Assert.Equal(second.GetProperty("id").GetGuid(), (await StartAsync(candidate, examId)).GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task AnExtraAttempt_CannotBeGrantedWhileTheCandidateStillHasOneToUse()
    {
        var (admin, candidate, candidateId, examId) = await EnrolledAsync();
        using var _a = admin;
        using var _c = candidate;

        await AssertProblemAsync(await GrantAsync(admin, examId, candidateId), HttpStatusCode.Conflict, "attempt_available");

        await SitAsync(candidate, examId, "Right");
        (await GrantAsync(admin, examId, candidateId)).EnsureSuccessStatusCode();
        // A second click, or a second administrator, before the candidate has used the first grant.
        await AssertProblemAsync(await GrantAsync(admin, examId, candidateId), HttpStatusCode.Conflict, "attempt_available");
        Assert.Equal(2, (await StaffRowAsync(admin, examId, candidateId)).GetProperty("attemptsAllowed").GetInt32());
    }

    [Fact]
    public async Task AGrant_NeedsAnEnrolledCandidate_AndAnExamThatExists()
    {
        var (admin, candidate, _, examId) = await EnrolledAsync();
        using var _a = admin;
        using var _c = candidate;
        var (stranger, strangerUser) = await factory.CandidateClientAsync();
        using var _s = stranger;

        await AssertProblemAsync(await GrantAsync(admin, examId, strangerUser.UserId), HttpStatusCode.NotFound, "candidate_not_enrolled");
        await AssertProblemAsync(await GrantAsync(admin, Guid.NewGuid(), strangerUser.UserId), HttpStatusCode.NotFound, "exam_not_found");
        await AssertProblemAsync(await admin.GetAsync($"/v1/exams/{Guid.NewGuid()}/attempts"), HttpStatusCode.NotFound, "exam_not_found");
    }

    [Fact]
    public async Task AReasonThatIsTooLong_IsRefusedWithAReason_AndGrantsNothing()
    {
        var (admin, candidate, candidateId, examId) = await EnrolledAsync();
        using var _a = admin;
        using var _c = candidate;
        await SitAsync(candidate, examId, "Right");

        await AssertProblemAsync(await GrantAsync(admin, examId, candidateId, new string('x', 501)), HttpStatusCode.BadRequest, "invalid_attempt");

        Assert.Equal(1, (await StaffRowAsync(admin, examId, candidateId)).GetProperty("attemptsAllowed").GetInt32());
    }

    [Fact]
    public async Task TheStaffList_ShowsTheCandidate_TheirAttemptsAndWhetherAnotherCanBeGiven()
    {
        var (admin, candidate, candidateId, examId) = await EnrolledAsync();
        using var _a = admin;
        using var _c = candidate;
        var before = await StaffRowAsync(admin, examId, candidateId);
        Assert.Equal((0, 1, false), (before.GetProperty("attemptsUsed").GetInt32(), before.GetProperty("attemptsAllowed").GetInt32(), before.GetProperty("canGrant").GetBoolean()));

        await SitAsync(candidate, examId, "Right");
        var after = await StaffRowAsync(admin, examId, candidateId);

        Assert.True(after.GetProperty("canGrant").GetBoolean());
        var attempt = Assert.Single(after.GetProperty("attempts").EnumerateArray());
        Assert.Equal((1, "Submitted", 1m), (attempt.GetProperty("number").GetInt32(), attempt.GetProperty("status").GetString(), attempt.GetProperty("score").GetDecimal()));
        Assert.StartsWith("candidate-", after.GetProperty("email").GetString());
        Assert.False((await admin.GetFromJsonAsync<JsonElement>($"/v1/exams/{examId}/attempts")).GetProperty("windowClosed").GetBoolean());
    }

    [Fact]
    public async Task ACandidate_CannotGrantAnAttempt_OrSeeWhoElseIsEnrolled()
    {
        var (admin, candidate, candidateId, examId) = await EnrolledAsync();
        using var _a = admin;
        using var _c = candidate;
        await SitAsync(candidate, examId, "Right");

        Assert.Equal(HttpStatusCode.Forbidden, (await GrantAsync(candidate, examId, candidateId)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await candidate.GetAsync($"/v1/exams/{examId}/attempts")).StatusCode);
        Assert.Equal(1, (await StaffRowAsync(admin, examId, candidateId)).GetProperty("attemptsAllowed").GetInt32());
    }

    [Fact]
    public async Task OneCandidatesGrant_DoesNotGiveAnotherCandidateAnAttempt()
    {
        var (admin, candidate, candidateId, examId) = await EnrolledAsync();
        using var _a = admin;
        using var _c = candidate;
        var (other, otherUser) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _o = other;
        await SitAsync(candidate, examId, "Right");
        await SitAsync(other, examId, "Right");

        (await GrantAsync(admin, examId, candidateId)).EnsureSuccessStatusCode();

        Assert.False((await MyExamAsync(other, examId)).GetProperty("canStartAttempt").GetBoolean());
        Assert.Equal(1, (await StaffRowAsync(admin, examId, otherUser.UserId)).GetProperty("attemptsAllowed").GetInt32());
        Assert.Equal(1, (await StartAsync(other, examId)).GetProperty("number").GetInt32());
    }

    [Fact]
    public async Task TwoStartsAtTheSameMoment_OfTheExtraAttempt_CreateOnlyOne()
    {
        var (admin, candidate, candidateId, examId) = await EnrolledAsync();
        using var _a = admin;
        using var _c = candidate;
        await SitAsync(candidate, examId, "Right");
        (await GrantAsync(admin, examId, candidateId)).EnsureSuccessStatusCode();

        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => candidate.PostAsync($"/v1/me/exams/{examId}/attempts", content: null)));

        // One creates attempt 2. The other either arrives after it and resumes it, or loses the race and is told so.
        Assert.All(results, r => Assert.True(r.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict, r.StatusCode.ToString()));
        Assert.Contains(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(2, (await StaffRowAsync(admin, examId, candidateId)).GetProperty("attemptsUsed").GetInt32());
    }

    [Fact]
    public async Task TwoAdministratorsGrantingAtTheSameMoment_GiveOneAttempt()
    {
        var (admin, candidate, candidateId, examId) = await EnrolledAsync();
        using var _a = admin;
        using var _c = candidate;
        await SitAsync(candidate, examId, "Right");

        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => GrantAsync(admin, examId, candidateId)));

        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Created);
        Assert.All(results.Where(r => r.StatusCode != HttpStatusCode.Created), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Equal(2, (await StaffRowAsync(admin, examId, candidateId)).GetProperty("attemptsAllowed").GetInt32());
    }
}
