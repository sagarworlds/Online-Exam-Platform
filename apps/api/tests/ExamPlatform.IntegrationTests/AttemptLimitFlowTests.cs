using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// The exam author's choice of how many attempts every enrolled candidate has (FR-12), over real HTTP and a real database:
/// the setting itself, its refusals and who may use it, and what the runtime then allows a candidate to do.
/// </summary>
public sealed class AttemptLimitFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await JsonAsync(response)).GetProperty("title").GetString());
    }

    private static async Task<Guid> PublishedExamAsync(HttpClient admin)
    {
        var question = await CreateQuestionAsync(admin, "Limit question", "Right", "Wrong");
        return await CreateExamAsync(admin, "Limit exam", [question], TimeSpan.FromMinutes(-5));
    }

    private static async Task<int> LimitOfAsync(HttpClient admin, Guid examId) =>
        (await admin.GetFromJsonAsync<JsonElement>($"/v1/exams/{examId}")).GetProperty("config").GetProperty("maxAttempts").GetInt32();

    // ---- the setting ------------------------------------------------------------------------------

    [Fact]
    public async Task AnExam_AllowsOneAttempt_UnlessTheAuthorSaysOtherwise()
    {
        using var admin = await factory.AdminClientAsync();
        var examId = await PublishedExamAsync(admin);

        Assert.Equal(1, await LimitOfAsync(admin, examId));
    }

    [Fact]
    public async Task TheAuthor_CanSetTheLimit_EvenOnceTheExamIsPublished_AndGetsTheExamBack()
    {
        using var admin = await factory.AdminClientAsync();
        var examId = await PublishedExamAsync(admin);

        var response = await admin.PutAsJsonAsync($"/v1/exams/{examId}/attempt-limit", new { maxAttempts = 3 });

        response.EnsureSuccessStatusCode();
        Assert.Equal(3, (await JsonAsync(response)).GetProperty("config").GetProperty("maxAttempts").GetInt32());
        Assert.Equal(3, await LimitOfAsync(admin, examId));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    [InlineData(11)]
    public async Task ANumberOutsideOneToTen_IsRefusedWithAReason_AndTheLimitStays(int attempts)
    {
        using var admin = await factory.AdminClientAsync();
        var examId = await PublishedExamAsync(admin);

        await AssertProblemAsync(
            await admin.PutAsJsonAsync($"/v1/exams/{examId}/attempt-limit", new { maxAttempts = attempts }), HttpStatusCode.BadRequest, "invalid_exam_config");

        Assert.Equal(1, await LimitOfAsync(admin, examId));
    }

    [Fact]
    public async Task ABodyWithNoNumber_IsRefusedWithAReason_NotACrash()
    {
        using var admin = await factory.AdminClientAsync();
        var examId = await PublishedExamAsync(admin);

        await AssertProblemAsync(await admin.PutAsJsonAsync($"/v1/exams/{examId}/attempt-limit", new { }), HttpStatusCode.BadRequest, "invalid_exam_config");
    }

    [Fact]
    public async Task ForAnExamThatDoesNotExist_TheRouteAnswersNotFound()
    {
        using var admin = await factory.AdminClientAsync();

        await AssertProblemAsync(
            await admin.PutAsJsonAsync($"/v1/exams/{Guid.NewGuid()}/attempt-limit", new { maxAttempts = 2 }), HttpStatusCode.NotFound, "exam_not_found");
    }

    [Fact]
    public async Task ACandidate_CannotSetTheLimit()
    {
        using var admin = await factory.AdminClientAsync();
        var examId = await PublishedExamAsync(admin);
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;

        var response = await candidate.PutAsJsonAsync($"/v1/exams/{examId}/attempt-limit", new { maxAttempts = 5 });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(1, await LimitOfAsync(admin, examId));
    }

    // ---- what the runtime then allows -------------------------------------------------------------

    private static async Task<JsonElement> StartAsync(HttpClient candidate, Guid examId)
    {
        var response = await candidate.PostAsJsonAsync($"/v1/me/exams/{examId}/attempts", new { instructionsAcknowledged = true });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await JsonAsync(response);
    }

    /// <summary>Starts (or resumes) an attempt and submits it straight away, returning the submitted attempt.</summary>
    private static async Task<JsonElement> SitAsync(HttpClient candidate, Guid examId)
    {
        var attempt = await StartAsync(candidate, examId);
        return await JsonAsync((await candidate.PostAsync($"/v1/me/attempts/{attempt.GetProperty("id").GetGuid()}/submit", content: null)).EnsureSuccessStatusCode());
    }

    private static Task<HttpResponseMessage> SetLimitAsync(HttpClient admin, Guid examId, int attempts) =>
        admin.PutAsJsonAsync($"/v1/exams/{examId}/attempt-limit", new { maxAttempts = attempts });

    private static Task<HttpResponseMessage> GrantAsync(HttpClient admin, Guid examId, Guid candidateId) =>
        admin.PostAsJsonAsync($"/v1/exams/{examId}/candidates/{candidateId}/extra-attempts", new { reason = "Power cut" });

    private static async Task<JsonElement> MyExamAsync(HttpClient candidate, Guid examId) =>
        (await candidate.GetFromJsonAsync<JsonElement>("/v1/me/exams")).EnumerateArray().Single(e => e.GetProperty("examId").GetGuid() == examId);

    private static async Task<JsonElement> StaffViewAsync(HttpClient admin, Guid examId) =>
        await admin.GetFromJsonAsync<JsonElement>($"/v1/exams/{examId}/attempts");

    [Fact]
    public async Task WithALimitOfTwo_ACandidateSitsTwiceWithNoGrant_AndAThirdStartOnlyShowsTheSecond()
    {
        using var admin = await factory.AdminClientAsync();
        var examId = await PublishedExamAsync(admin);
        (await SetLimitAsync(admin, examId, 2)).EnsureSuccessStatusCode();
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;

        var first = await SitAsync(candidate, examId);
        var second = await StartAsync(candidate, examId);

        Assert.Equal(1, first.GetProperty("number").GetInt32());
        Assert.Equal(2, second.GetProperty("number").GetInt32());
        Assert.Equal("InProgress", second.GetProperty("status").GetString());
        Assert.NotEqual(first.GetProperty("id").GetGuid(), second.GetProperty("id").GetGuid());

        (await candidate.PostAsync($"/v1/me/attempts/{second.GetProperty("id").GetGuid()}/submit", content: null)).EnsureSuccessStatusCode();
        var third = await StartAsync(candidate, examId);

        // Both are used: the call stays safe to repeat and creates nothing nobody allowed.
        Assert.Equal(second.GetProperty("id").GetGuid(), third.GetProperty("id").GetGuid());
        Assert.Equal("Submitted", third.GetProperty("status").GetString());
        var mine = await MyExamAsync(candidate, examId);
        Assert.Equal((2, 2, false), (mine.GetProperty("attemptsAllowed").GetInt32(), mine.GetProperty("attemptsUsed").GetInt32(), mine.GetProperty("canStartAttempt").GetBoolean()));
    }

    [Fact]
    public async Task AnExamNobodyChangedTheLimitOf_StillGivesOneAttempt()
    {
        using var admin = await factory.AdminClientAsync();
        var examId = await PublishedExamAsync(admin);
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;
        var first = await SitAsync(candidate, examId);

        var again = await StartAsync(candidate, examId);

        Assert.Equal(first.GetProperty("id").GetGuid(), again.GetProperty("id").GetGuid());
        Assert.Equal(1, (await MyExamAsync(candidate, examId)).GetProperty("attemptsAllowed").GetInt32());
    }

    [Fact]
    public async Task RaisingTheLimitLater_OpensAnotherAttemptAtOnce()
    {
        using var admin = await factory.AdminClientAsync();
        var examId = await PublishedExamAsync(admin);
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;
        await SitAsync(candidate, examId);
        Assert.False((await MyExamAsync(candidate, examId)).GetProperty("canStartAttempt").GetBoolean());

        (await SetLimitAsync(admin, examId, 2)).EnsureSuccessStatusCode();

        Assert.True((await MyExamAsync(candidate, examId)).GetProperty("canStartAttempt").GetBoolean());
        Assert.Equal(2, (await StartAsync(candidate, examId)).GetProperty("number").GetInt32());
    }

    [Fact]
    public async Task AnAdministratorGrant_StillAddsOneOnTopOfTheLimit_AndTheStaffViewReportsBoth()
    {
        using var admin = await factory.AdminClientAsync();
        var examId = await PublishedExamAsync(admin);
        (await SetLimitAsync(admin, examId, 2)).EnsureSuccessStatusCode();
        var (candidate, user) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;
        await SitAsync(candidate, examId);

        // One attempt of the limit is still unused, so a grant is refused as before.
        await AssertProblemAsync(await GrantAsync(admin, examId, user.UserId), HttpStatusCode.Conflict, "attempt_available");

        await SitAsync(candidate, examId);
        var granted = await GrantAsync(admin, examId, user.UserId);

        Assert.Equal(HttpStatusCode.Created, granted.StatusCode);
        var view = await StaffViewAsync(admin, examId);
        Assert.Equal(2, view.GetProperty("attemptsPerCandidate").GetInt32());
        var row = view.GetProperty("candidates").EnumerateArray().Single(c => c.GetProperty("candidateId").GetGuid() == user.UserId);
        Assert.Equal((3, 2), (row.GetProperty("attemptsAllowed").GetInt32(), row.GetProperty("attemptsUsed").GetInt32()));
        Assert.Equal(3, (await StartAsync(candidate, examId)).GetProperty("number").GetInt32());
    }

    [Fact]
    public async Task LoweringTheLimitBelowWhatWasMade_TakesNothingBack_AllowsNoMore_AndRefusesAUselessGrant()
    {
        using var admin = await factory.AdminClientAsync();
        var examId = await PublishedExamAsync(admin);
        (await SetLimitAsync(admin, examId, 3)).EnsureSuccessStatusCode();
        var (candidate, user) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;
        var first = await SitAsync(candidate, examId);
        var second = await SitAsync(candidate, examId);

        (await SetLimitAsync(admin, examId, 1)).EnsureSuccessStatusCode();

        var mine = await MyExamAsync(candidate, examId);
        Assert.Equal((1, 2, false), (mine.GetProperty("attemptsAllowed").GetInt32(), mine.GetProperty("attemptsUsed").GetInt32(), mine.GetProperty("canStartAttempt").GetBoolean()));
        Assert.Equal([first.GetProperty("id").GetGuid(), second.GetProperty("id").GetGuid()], mine.GetProperty("attempts").EnumerateArray().Select(a => a.GetProperty("id").GetGuid()));
        Assert.Equal(second.GetProperty("id").GetGuid(), (await StartAsync(candidate, examId)).GetProperty("id").GetGuid());

        // A grant would still leave them over the limit, so it is refused with the way out rather than recorded for nothing.
        await AssertProblemAsync(await GrantAsync(admin, examId, user.UserId), HttpStatusCode.Conflict, "attempt_over_limit");
        var staff = (await StaffViewAsync(admin, examId)).GetProperty("candidates").EnumerateArray().Single(c => c.GetProperty("candidateId").GetGuid() == user.UserId);
        Assert.False(staff.GetProperty("canGrant").GetBoolean());

        (await SetLimitAsync(admin, examId, 3)).EnsureSuccessStatusCode();
        Assert.Equal(3, (await StartAsync(candidate, examId)).GetProperty("number").GetInt32());
    }
}
