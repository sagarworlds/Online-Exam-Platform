using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// Correcting a question's answer key after candidates have answered it (FR-31): every submitted attempt it affects is
/// rescored, and the candidate's review shows that their result changed, and why.
/// </summary>
public sealed class AnswerKeyCorrectionFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task CorrectingTheKey_RescoresEverySubmittedAttempt_AndTheCandidateSeesWhatChanged()
    {
        using var admin = await factory.AdminClientAsync();
        // The question bank's rule marks the first option correct; the key is wrong on purpose, so the candidate's
        // genuinely right answer is first marked wrong.
        var questionId = await CreateQuestionAsync(admin, "Capital of France?", "Rome", "Paris");
        var examId = await CreateExamAsync(admin, "Geography", [questionId], TimeSpan.FromMinutes(-5));
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;

        var attemptId = (await JsonAsync((await candidate.PostAsJsonAsync($"/v1/me/exams/{examId}/attempts", new { instructionsAcknowledged = true })).EnsureSuccessStatusCode())).GetProperty("id").GetGuid();
        var attempt = await candidate.GetFromJsonAsync<JsonElement>($"/v1/me/attempts/{attemptId}");
        var question = attempt.GetProperty("sections")[0].GetProperty("questions")[0];
        var paris = question.GetProperty("options").EnumerateArray().Single(o => o.GetProperty("text").GetString() == "Paris").GetProperty("id").GetGuid();
        (await candidate.PutAsJsonAsync($"/v1/me/attempts/{attemptId}/answers/{question.GetProperty("id").GetGuid()}", new { optionId = paris })).EnsureSuccessStatusCode();
        var submitted = await JsonAsync((await candidate.PostAsync($"/v1/me/attempts/{attemptId}/submit", content: null)).EnsureSuccessStatusCode());

        // Scored against the wrong key: the candidate chose Paris, which was not marked correct.
        Assert.Equal(0m, submitted.GetProperty("score").GetDecimal());

        var correction = await JsonAsync((await admin.PostAsJsonAsync(
            $"/v1/questions/{questionId}/correct-answer-key",
            new { correctOptionIds = new[] { paris }, reason = "Paris is the capital of France, not Rome" })).EnsureSuccessStatusCode());
        Assert.True(correction.GetProperty("keyChanged").GetBoolean());
        Assert.Equal(1, correction.GetProperty("attemptsRescored").GetInt32());

        var review = await JsonAsync((await candidate.GetAsync($"/v1/me/attempts/{attemptId}/review")).EnsureSuccessStatusCode());
        Assert.Equal(1m, review.GetProperty("score").GetDecimal());
        var revision = Assert.Single(review.GetProperty("revisions").EnumerateArray());
        Assert.Equal(0m, revision.GetProperty("previousScore").GetDecimal());
        Assert.Equal(1m, revision.GetProperty("newScore").GetDecimal());
        Assert.Equal("Paris is the capital of France, not Rome", revision.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task CorrectingTheKeyToTheSameAnswer_RescoresNothing_AndNoRevisionAppears()
    {
        using var admin = await factory.AdminClientAsync();
        var questionId = await CreateQuestionAsync(admin, "2 + 2?", "4", "5");
        var examId = await CreateExamAsync(admin, "Maths", [questionId], TimeSpan.FromMinutes(-5));
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;

        var attemptId = (await JsonAsync((await candidate.PostAsJsonAsync($"/v1/me/exams/{examId}/attempts", new { instructionsAcknowledged = true })).EnsureSuccessStatusCode())).GetProperty("id").GetGuid();
        var attempt = await candidate.GetFromJsonAsync<JsonElement>($"/v1/me/attempts/{attemptId}");
        var question = attempt.GetProperty("sections")[0].GetProperty("questions")[0];
        var four = question.GetProperty("options").EnumerateArray().Single(o => o.GetProperty("text").GetString() == "4").GetProperty("id").GetGuid();
        (await candidate.PutAsJsonAsync($"/v1/me/attempts/{attemptId}/answers/{question.GetProperty("id").GetGuid()}", new { optionId = four })).EnsureSuccessStatusCode();
        await (await candidate.PostAsync($"/v1/me/attempts/{attemptId}/submit", content: null)).Content.ReadAsStringAsync();

        var correction = await JsonAsync((await admin.PostAsJsonAsync(
            $"/v1/questions/{questionId}/correct-answer-key",
            new { correctOptionIds = new[] { four }, reason = "Already right" })).EnsureSuccessStatusCode());

        Assert.False(correction.GetProperty("keyChanged").GetBoolean());
        Assert.Equal(0, correction.GetProperty("attemptsRescored").GetInt32());
        var review = await JsonAsync((await candidate.GetAsync($"/v1/me/attempts/{attemptId}/review")).EnsureSuccessStatusCode());
        Assert.Empty(review.GetProperty("revisions").EnumerateArray());
    }

    [Fact]
    public async Task WithoutTheQuestionManagePermission_CorrectingTheKey_IsRefused()
    {
        using var admin = await factory.AdminClientAsync();
        var questionId = await CreateQuestionAsync(admin, "Q?", "A", "B");
        var (candidate, _) = await factory.CandidateClientAsync();
        using var _c = candidate;

        var response = await candidate.PostAsJsonAsync(
            $"/v1/questions/{questionId}/correct-answer-key", new { correctOptionIds = Array.Empty<Guid>(), reason = "x" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
