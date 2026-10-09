using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// The author sets how many times a candidate may leave the exam page (FR-22); the page reports each departure, and the server
/// counts them, tells the page how many are left, and ends the attempt at the limit.
/// </summary>
public sealed class FocusViolationFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<Guid> PublishedExamAsync(HttpClient admin)
    {
        var question = await CreateQuestionAsync(admin, "What is 2 + 2?", "4", "5");
        return await CreateExamAsync(admin, "Focus Exam", [question], startsIn: TimeSpan.FromMinutes(-5));
    }

    private static async Task<JsonElement> ExamAsync(HttpClient admin, Guid examId) =>
        await admin.GetFromJsonAsync<JsonElement>($"/v1/exams/{examId}");

    private static async Task<Guid> StartAsync(HttpClient candidate, Guid examId)
    {
        var response = await candidate.PostAsJsonAsync($"/v1/me/exams/{examId}/attempts", new { instructionsAcknowledged = true });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> LeaveAsync(HttpClient candidate, Guid attemptId, string kind = "TabHidden") =>
        candidate.PostAsJsonAsync($"/v1/me/attempts/{attemptId}/focus-violations", new { kind });

    [Fact]
    public async Task ANewExam_DoesNotWatch()
    {
        using var admin = await factory.AdminClientAsync();
        var examId = await PublishedExamAsync(admin);

        Assert.Equal(0, (await ExamAsync(admin, examId)).GetProperty("config").GetProperty("focusViolationLimit").GetInt32());
    }

    [Fact]
    public async Task TheAuthor_CanSetTheLimit_EvenAfterPublishing_AndTurnItOff()
    {
        using var admin = await factory.AdminClientAsync();
        var examId = await PublishedExamAsync(admin);

        var set = await admin.PutAsJsonAsync($"/v1/exams/{examId}/focus-violation-limit", new { focusViolationLimit = 3 });

        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        Assert.Equal(3, (await ExamAsync(admin, examId)).GetProperty("config").GetProperty("focusViolationLimit").GetInt32());

        await admin.PutAsJsonAsync($"/v1/exams/{examId}/focus-violation-limit", new { focusViolationLimit = 0 });
        Assert.Equal(0, (await ExamAsync(admin, examId)).GetProperty("config").GetProperty("focusViolationLimit").GetInt32());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"focusViolationLimit\": -1}")]
    [InlineData("{\"focusViolationLimit\": 21}")]
    public async Task AMissingOrOutOfRangeLimit_IsA400_AndTheOldOneStays(string body)
    {
        using var admin = await factory.AdminClientAsync();
        var examId = await PublishedExamAsync(admin);
        await admin.PutAsJsonAsync($"/v1/exams/{examId}/focus-violation-limit", new { focusViolationLimit = 2 });

        var response = await admin.PutAsync($"/v1/exams/{examId}/focus-violation-limit",
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(2, (await ExamAsync(admin, examId)).GetProperty("config").GetProperty("focusViolationLimit").GetInt32());
    }

    [Fact]
    public async Task AnUnknownExam_Is404()
    {
        using var admin = await factory.AdminClientAsync();

        var response = await admin.PutAsJsonAsync($"/v1/exams/{Guid.NewGuid()}/focus-violation-limit", new { focusViolationLimit = 3 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task TheServer_CountsEachDeparture_ThenEndsTheAttemptAtTheLimit()
    {
        using var admin = await factory.AdminClientAsync();
        var examId = await PublishedExamAsync(admin);
        await admin.PutAsJsonAsync($"/v1/exams/{examId}/focus-violation-limit", new { focusViolationLimit = 2 });
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;
        var attemptId = await StartAsync(candidate, examId);

        var first = await (await LeaveAsync(candidate, attemptId)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, first.GetProperty("violations").GetInt32());
        Assert.Equal(2, first.GetProperty("limit").GetInt32());
        Assert.False(first.GetProperty("attemptEnded").GetBoolean());

        var reloaded = await candidate.GetFromJsonAsync<JsonElement>($"/v1/me/attempts/{attemptId}");
        Assert.Equal(1, reloaded.GetProperty("focusViolations").GetInt32());
        Assert.Equal(2, reloaded.GetProperty("focusViolationLimit").GetInt32());

        var second = await (await LeaveAsync(candidate, attemptId, "FullscreenExited")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(second.GetProperty("attemptEnded").GetBoolean());

        var ended = await candidate.GetFromJsonAsync<JsonElement>($"/v1/me/attempts/{attemptId}");
        Assert.Equal("Submitted", ended.GetProperty("status").GetString());
        Assert.True(ended.GetProperty("endedByViolations").GetBoolean());
        Assert.True(ended.GetProperty("autoSubmitted").GetBoolean());

        // Nothing more can be reported once it is over.
        Assert.Equal(HttpStatusCode.Conflict, (await LeaveAsync(candidate, attemptId)).StatusCode);
    }

    [Fact]
    public async Task AnExamThatDoesNotWatch_AcceptsTheReport_ButRecordsNothing()
    {
        using var admin = await factory.AdminClientAsync();
        var examId = await PublishedExamAsync(admin);
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;
        var attemptId = await StartAsync(candidate, examId);

        var result = await (await LeaveAsync(candidate, attemptId)).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(0, result.GetProperty("violations").GetInt32());
        Assert.Equal(0, result.GetProperty("limit").GetInt32());
        Assert.False(result.GetProperty("attemptEnded").GetBoolean());
        Assert.Equal("InProgress", (await candidate.GetFromJsonAsync<JsonElement>($"/v1/me/attempts/{attemptId}")).GetProperty("status").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("Sneezed")]
    [InlineData("1")]
    [InlineData("99")]
    public async Task AKindThatIsNotOneOfTheNames_IsA400(string kind)
    {
        using var admin = await factory.AdminClientAsync();
        var examId = await PublishedExamAsync(admin);
        await admin.PutAsJsonAsync($"/v1/exams/{examId}/focus-violation-limit", new { focusViolationLimit = 5 });
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;
        var attemptId = await StartAsync(candidate, examId);

        var response = await LeaveAsync(candidate, attemptId, kind);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, (await candidate.GetFromJsonAsync<JsonElement>($"/v1/me/attempts/{attemptId}")).GetProperty("focusViolations").GetInt32());
    }

    [Fact]
    public async Task SomeoneElsesAttempt_Is404()
    {
        using var admin = await factory.AdminClientAsync();
        var examId = await PublishedExamAsync(admin);
        await admin.PutAsJsonAsync($"/v1/exams/{examId}/focus-violation-limit", new { focusViolationLimit = 1 });
        var (owner, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        var (other, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _o = owner;
        using var _p = other;
        var attemptId = await StartAsync(owner, examId);

        var response = await LeaveAsync(other, attemptId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("InProgress", (await owner.GetFromJsonAsync<JsonElement>($"/v1/me/attempts/{attemptId}")).GetProperty("status").GetString());
    }

    [Fact]
    public async Task TheInstructionsPage_IsToldTheLimit()
    {
        using var admin = await factory.AdminClientAsync();
        var examId = await PublishedExamAsync(admin);
        await admin.PutAsJsonAsync($"/v1/exams/{examId}/focus-violation-limit", new { focusViolationLimit = 4 });
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;

        var exams = await candidate.GetFromJsonAsync<JsonElement>("/v1/me/exams");
        var mine = exams.EnumerateArray().Single(e => e.GetProperty("examId").GetGuid() == examId);

        Assert.Equal(4, mine.GetProperty("rules").GetProperty("focusViolationLimit").GetInt32());
    }
}
