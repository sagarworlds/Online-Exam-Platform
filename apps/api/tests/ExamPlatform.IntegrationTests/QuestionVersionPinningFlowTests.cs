using System.Net.Http.Json;
using System.Text.Json;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// An attempt reads its questions at the version it began with (FR-7): editing a question afterwards changes what later attempts see,
/// never what an attempt already sitting or finished shows and is marked against, except an answer-key correction, which moves the
/// attempts it rescores to the corrected version on purpose (FR-31).
/// </summary>
public sealed class QuestionVersionPinningFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<JsonElement> StartAsync(HttpClient candidate, Guid examId) =>
        await JsonAsync((await candidate.PostAsJsonAsync($"/v1/me/exams/{examId}/attempts", new { instructionsAcknowledged = true })).EnsureSuccessStatusCode());

    private static string TextOf(JsonElement attempt) => attempt.GetProperty("sections")[0].GetProperty("questions")[0].GetProperty("text").GetString()!;

    /// <summary>Rewrites a question's wording, leaving its options, ids and key exactly as they are.</summary>
    private static async Task RewordAsync(HttpClient admin, Guid questionId, string text)
    {
        var stored = await admin.GetFromJsonAsync<JsonElement>($"/v1/questions/{questionId}");
        var options = stored.GetProperty("options").EnumerateArray()
            .Select(o => new { id = o.GetProperty("id").GetGuid(), text = o.GetProperty("text").GetString(), isCorrect = o.GetProperty("isCorrect").GetBoolean() })
            .ToArray();
        (await admin.PutAsJsonAsync($"/v1/questions/{questionId}", new { text, options })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task EditingAQuestionAfterAnAttemptStarted_ChangesOnlyAttemptsStartedLater()
    {
        using var admin = await factory.AdminClientAsync();
        var questionId = await CreateQuestionAsync(admin, "Original wording?", "A", "B");
        var examId = await CreateExamAsync(admin, "Pinning", [questionId], TimeSpan.FromMinutes(-5));
        var (first, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        var (second, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _a = first;
        using var _b = second;
        var firstAttempt = await StartAsync(first, examId);

        await RewordAsync(admin, questionId, "Edited wording?");

        var firstAgain = await first.GetFromJsonAsync<JsonElement>($"/v1/me/attempts/{firstAttempt.GetProperty("id").GetGuid()}");
        Assert.Contains("Original wording?", TextOf(firstAgain));
        Assert.Contains("Edited wording?", TextOf(await StartAsync(second, examId)));
    }

    [Fact]
    public async Task AFinishedAttempt_KeepsItsWordingInTheReview_UntilTheKeyIsCorrected()
    {
        using var admin = await factory.AdminClientAsync();
        // The first option is the one marked correct, so the key is wrong on purpose.
        var questionId = await CreateQuestionAsync(admin, "Capital of France?", "Rome", "Paris");
        var examId = await CreateExamAsync(admin, "Pinning review", [questionId], TimeSpan.FromMinutes(-5));
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;
        var started = await StartAsync(candidate, examId);
        var attemptId = started.GetProperty("id").GetGuid();
        var question = started.GetProperty("sections")[0].GetProperty("questions")[0];
        var paris = question.GetProperty("options").EnumerateArray().Single(o => o.GetProperty("text").GetString() == "Paris").GetProperty("id").GetGuid();
        (await candidate.PutAsJsonAsync($"/v1/me/attempts/{attemptId}/answers/{question.GetProperty("id").GetGuid()}", new { optionId = paris })).EnsureSuccessStatusCode();
        var submitted = await JsonAsync((await candidate.PostAsync($"/v1/me/attempts/{attemptId}/submit", content: null)).EnsureSuccessStatusCode());
        Assert.Equal(0m, submitted.GetProperty("score").GetDecimal());

        await RewordAsync(admin, questionId, "What is the capital of France?");

        var review = await JsonAsync((await candidate.GetAsync($"/v1/me/attempts/{attemptId}/review")).EnsureSuccessStatusCode());
        Assert.Contains("Capital of France?", review.GetProperty("sections")[0].GetProperty("questions")[0].GetProperty("text").GetString());
        Assert.DoesNotContain("What is the capital", review.GetProperty("sections")[0].GetProperty("questions")[0].GetProperty("text").GetString());

        (await admin.PostAsJsonAsync($"/v1/questions/{questionId}/correct-answer-key", new { correctOptionIds = new[] { paris }, reason = "Wrong key" })).EnsureSuccessStatusCode();

        review = await JsonAsync((await candidate.GetAsync($"/v1/me/attempts/{attemptId}/review")).EnsureSuccessStatusCode());
        Assert.Equal(1m, review.GetProperty("score").GetDecimal());
        Assert.Contains("What is the capital of France?", review.GetProperty("sections")[0].GetProperty("questions")[0].GetProperty("text").GetString());
    }
}
