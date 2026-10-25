using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// How strictly a typed answer is compared (FR-5), through the API and the database: the rule is stored with the question and its versions,
/// marks a candidate's answer at submit, cannot be changed by an edit once the question is answered, and a correction changes it and
/// rescores the people already marked.
/// </summary>
public sealed class TextMatchRuleFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<Guid> CreateTextQuestionAsync(HttpClient admin, object body)
    {
        var created = await admin.PostAsJsonAsync("/v1/questions", body);
        await ServerErrorLog.EnsureSuccessAsync(created);
        return (await JsonAsync(created)).GetProperty("id").GetGuid();
    }

    private static async Task<(Guid AttemptId, Guid QuestionId)> TypeAndSubmitAsync(HttpClient candidate, Guid examId, string typed)
    {
        var started = await candidate.PostAsJsonAsync($"/v1/me/exams/{examId}/attempts", new { instructionsAcknowledged = true });
        var attemptId = (await JsonAsync(started.EnsureSuccessStatusCode())).GetProperty("id").GetGuid();
        var attempt = await candidate.GetFromJsonAsync<JsonElement>($"/v1/me/attempts/{attemptId}");
        var questionId = attempt.GetProperty("sections")[0].GetProperty("questions")[0].GetProperty("id").GetGuid();

        (await candidate.PutAsJsonAsync($"/v1/me/attempts/{attemptId}/answers/{questionId}", new { text = typed })).EnsureSuccessStatusCode();
        (await candidate.PostAsync($"/v1/me/attempts/{attemptId}/submit", content: null)).EnsureSuccessStatusCode();
        return (attemptId, questionId);
    }

    private static async Task<decimal> ScoreAsync(HttpClient candidate, Guid attemptId) =>
        (await candidate.GetFromJsonAsync<JsonElement>($"/v1/me/attempts/{attemptId}")).GetProperty("score").GetDecimal();

    [Fact]
    public async Task AQuestionWithNoRuleGiven_IsExact_AndReportsItsRule()
    {
        using var admin = await factory.AdminClientAsync();

        var id = await CreateTextQuestionAsync(admin, new { text = "Capital of France?", isTextAnswer = true, acceptedAnswers = new[] { "Paris" } });

        var stored = await admin.GetFromJsonAsync<JsonElement>($"/v1/questions/{id}");
        Assert.Equal("Exact", stored.GetProperty("matchRule").GetString());
    }

    [Fact]
    public async Task ALenientQuestion_ReportsItsRule_AndItsHistoryKeepsTheRuleOfEachVersion()
    {
        using var admin = await factory.AdminClientAsync();
        var id = await CreateTextQuestionAsync(admin, new { text = "Capital of France?", isTextAnswer = true, acceptedAnswers = new[] { "Paris" }, matchRule = "Lenient" });

        var stored = await admin.GetFromJsonAsync<JsonElement>($"/v1/questions/{id}");
        var history = await admin.GetFromJsonAsync<JsonElement>($"/v1/questions/{id}/history");

        Assert.Equal("Lenient", stored.GetProperty("matchRule").GetString());
        Assert.All(history.EnumerateArray(), version => Assert.Equal("Lenient", version.GetProperty("matchRule").GetString()));
    }

    [Fact]
    public async Task ARuleThatIsNotOneOfTheTwo_IsRefusedAsABadRequest()
    {
        using var admin = await factory.AdminClientAsync();

        var response = await admin.PostAsJsonAsync("/v1/questions", new { text = "Q?", isTextAnswer = true, acceptedAnswers = new[] { "A" }, matchRule = "Sloppy" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ARuleOnAMultipleChoiceQuestion_IsRefused()
    {
        using var admin = await factory.AdminClientAsync();

        var response = await admin.PostAsJsonAsync("/v1/questions", new
        {
            text = "Q?",
            options = new[] { new { text = "A", isCorrect = true }, new { text = "B", isCorrect = false } },
            matchRule = "Lenient",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Marking_FollowsTheQuestionsRule_WhenTheCandidateAddsAFullStop()
    {
        using var admin = await factory.AdminClientAsync();
        var lenient = await CreateTextQuestionAsync(admin, new { text = "Capital of France?", isTextAnswer = true, acceptedAnswers = new[] { "Paris" }, matchRule = "Lenient" });
        var exact = await CreateTextQuestionAsync(admin, new { text = "Capital of France? (exact)", isTextAnswer = true, acceptedAnswers = new[] { "Paris" } });
        var lenientExam = await CreateExamAsync(admin, "Lenient", [lenient], TimeSpan.FromMinutes(-5));
        var exactExam = await CreateExamAsync(admin, "Exact", [exact], TimeSpan.FromMinutes(-5));
        var (candidateA, _) = await factory.EnrollNewCandidateAsync(admin, lenientExam);
        using var _a = candidateA;
        var (candidateB, _) = await factory.EnrollNewCandidateAsync(admin, exactExam);
        using var _b = candidateB;

        var (lenientAttempt, _) = await TypeAndSubmitAsync(candidateA, lenientExam, "  paris. ");
        var (exactAttempt, _) = await TypeAndSubmitAsync(candidateB, exactExam, "  paris. ");

        Assert.Equal(1m, await ScoreAsync(candidateA, lenientAttempt));
        Assert.Equal(0m, await ScoreAsync(candidateB, exactAttempt));
    }

    [Fact]
    public async Task AnEditThatSendsNoRule_KeepsTheQuestionsRule()
    {
        using var admin = await factory.AdminClientAsync();
        var id = await CreateTextQuestionAsync(admin, new { text = "Capital of France?", isTextAnswer = true, acceptedAnswers = new[] { "Paris" }, matchRule = "Lenient" });

        var edited = await admin.PutAsJsonAsync($"/v1/questions/{id}", new { text = "What is the capital of France?", isTextAnswer = true, acceptedAnswers = new[] { "Paris" } });

        await ServerErrorLog.EnsureSuccessAsync(edited);
        Assert.Equal("Lenient", (await JsonAsync(edited)).GetProperty("matchRule").GetString());
    }

    [Fact]
    public async Task OnceAnswered_AnEditCannotChangeTheRule_ButACorrectionCan_AndRescoresWhoWasAlreadyMarked()
    {
        using var admin = await factory.AdminClientAsync();
        var id = await CreateTextQuestionAsync(admin, new { text = "Capital of France?", isTextAnswer = true, acceptedAnswers = new[] { "Paris" } });
        var examId = await CreateExamAsync(admin, "Correct the rule", [id], TimeSpan.FromMinutes(-5));
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;
        var (attemptId, _) = await TypeAndSubmitAsync(candidate, examId, "Paris.");
        Assert.Equal(0m, await ScoreAsync(candidate, attemptId));

        var edit = await admin.PutAsJsonAsync($"/v1/questions/{id}", new { text = "Capital of France?", isTextAnswer = true, acceptedAnswers = new[] { "Paris" }, matchRule = "Lenient" });
        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);

        var correction = await admin.PostAsJsonAsync(
            $"/v1/questions/{id}/correct-answer-key",
            new { reason = "Stray punctuation should not cost a mark.", acceptedAnswers = new[] { "Paris" }, matchRule = "Lenient" });
        await ServerErrorLog.EnsureSuccessAsync(correction);
        var result = await JsonAsync(correction);

        Assert.True(result.GetProperty("keyChanged").GetBoolean());
        Assert.Equal(1, result.GetProperty("attemptsRescored").GetInt32());
        Assert.Equal(1m, await ScoreAsync(candidate, attemptId));
        Assert.Equal("Lenient", (await admin.GetFromJsonAsync<JsonElement>($"/v1/questions/{id}")).GetProperty("matchRule").GetString());
    }
}
