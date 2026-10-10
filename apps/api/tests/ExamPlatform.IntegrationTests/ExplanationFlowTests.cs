using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// A question's explanation (FR-33) over real HTTP and a real database: it reaches a candidate only in the answer review, once the exam's
/// author has released the results, and the review shows the explanation of the version the candidate sat, however the author edits it later.
/// </summary>
public sealed class ExplanationFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Explanation = "Paris has been the seat of government for centuries.";

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    /// <summary>Creates a text-free two-option question ("Right" is correct) with an explanation, and returns it as the API answers.</summary>
    private static async Task<JsonElement> CreateWithExplanationAsync(HttpClient admin, string text, string explanation)
    {
        var response = await admin.PostAsJsonAsync("/v1/questions", new
        {
            text,
            options = new[] { new { text = "Right", isCorrect = true }, new { text = "Wrong", isCorrect = false } },
            explanation,
        });
        response.EnsureSuccessStatusCode();
        return await JsonAsync(response);
    }

    /// <summary>Starts an attempt, chooses the right option, and submits; returns the attempt's id.</summary>
    private static async Task<Guid> SitRightAsync(HttpClient candidate, Guid examId)
    {
        var started = await candidate.PostAsJsonAsync($"/v1/me/exams/{examId}/attempts", new { instructionsAcknowledged = true });
        var attemptId = (await JsonAsync(started.EnsureSuccessStatusCode())).GetProperty("id").GetGuid();

        var attempt = await candidate.GetFromJsonAsync<JsonElement>($"/v1/me/attempts/{attemptId}");
        var question = attempt.GetProperty("sections").EnumerateArray().SelectMany(s => s.GetProperty("questions").EnumerateArray()).Single();
        var right = question.GetProperty("options").EnumerateArray().Single(o => o.GetProperty("text").GetString() == "Right").GetProperty("id").GetGuid();
        (await candidate.PutAsJsonAsync($"/v1/me/attempts/{attemptId}/answers/{question.GetProperty("id").GetGuid()}", new { optionId = right }))
            .EnsureSuccessStatusCode();

        (await candidate.PostAsync($"/v1/me/attempts/{attemptId}/submit", content: null)).EnsureSuccessStatusCode();
        return attemptId;
    }

    private static JsonElement OnlyReviewedQuestion(JsonElement review) =>
        review.GetProperty("sections").EnumerateArray().SelectMany(s => s.GetProperty("questions").EnumerateArray()).Single();

    [Fact]
    public async Task TheExplanation_IsHeldBack_UntilTheResultsAreReleased_AndThenShownInTheReview()
    {
        var admin = await factory.AdminClientAsync();
        using var _a = admin;
        var question = await CreateWithExplanationAsync(admin, "Capital of France?", Explanation);
        Assert.Equal(Explanation, question.GetProperty("explanation").GetString());
        var examId = await CreateExamAsync(admin, "Explained exam", new List<Guid> { question.GetProperty("id").GetGuid() }, TimeSpan.FromMinutes(-5));
        (await admin.PutAsJsonAsync($"/v1/exams/{examId}/result-release", new { mode = "Scheduled", releaseTime = DateTime.UtcNow.AddDays(2) })).EnsureSuccessStatusCode();
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;
        var attemptId = await SitRightAsync(candidate, examId);

        // Before the release the review is refused, and the sitting never carried the explanation.
        var refused = await candidate.GetAsync($"/v1/me/attempts/{attemptId}/review");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("results_not_released", (await JsonAsync(refused)).GetProperty("title").GetString());
        var sitting = await (await candidate.GetAsync($"/v1/me/attempts/{attemptId}")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("seat of government", sitting);

        // Once released, the review shows it.
        (await admin.PutAsJsonAsync($"/v1/exams/{examId}/result-release", new { mode = "Instant" })).EnsureSuccessStatusCode();
        var review = await JsonAsync((await candidate.GetAsync($"/v1/me/attempts/{attemptId}/review")).EnsureSuccessStatusCode());
        Assert.Equal(Explanation, OnlyReviewedQuestion(review).GetProperty("explanation").GetString());
    }

    [Fact]
    public async Task AnExplanationEditedAfterTheSitting_DoesNotChangeWhatTheCandidateReviews()
    {
        var admin = await factory.AdminClientAsync();
        using var _a = admin;
        var question = await CreateWithExplanationAsync(admin, "Capital of France?", "First explanation.");
        var questionId = question.GetProperty("id").GetGuid();
        var examId = await CreateExamAsync(admin, "Versioned explanation", new List<Guid> { questionId }, TimeSpan.FromMinutes(-5));
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;
        var attemptId = await SitRightAsync(candidate, examId);

        // The author corrects the explanation after the candidate sat the exam. The options keep their ids, so the sitting still points at them.
        var options = question.GetProperty("options").EnumerateArray().Select(o => new
        {
            id = o.GetProperty("id").GetGuid(),
            text = o.GetProperty("text").GetString(),
            isCorrect = o.GetProperty("isCorrect").GetBoolean(),
        }).ToArray();
        (await admin.PutAsJsonAsync($"/v1/questions/{questionId}", new { text = "Capital of France?", options, explanation = "Second explanation." }))
            .EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync($"/v1/exams/{examId}/result-release", new { mode = "Instant" })).EnsureSuccessStatusCode();

        var review = await JsonAsync((await candidate.GetAsync($"/v1/me/attempts/{attemptId}/review")).EnsureSuccessStatusCode());
        Assert.Equal("First explanation.", OnlyReviewedQuestion(review).GetProperty("explanation").GetString());
    }
}
