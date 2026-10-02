using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// The answer review (FR-32, FR-33) over real HTTP and a real database: a candidate who has finished an exam sees which of
/// their answers were right once the exam's author allows it, and nobody sees the key before that.
/// </summary>
public sealed class AnswerReviewFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await JsonAsync(response)).GetProperty("title").GetString());
    }

    /// <summary>A published exam of three questions ("Q1", "Q2", "Q3"; the option "Right" is correct) and a candidate invited to it.</summary>
    private async Task<(HttpClient Admin, HttpClient Candidate, Guid ExamId)> EnrolledAsync()
    {
        var admin = await factory.AdminClientAsync();
        var questions = new List<Guid>();
        foreach (var text in new[] { "Q1", "Q2", "Q3" })
            questions.Add(await CreateQuestionAsync(admin, text, "Right", "Wrong"));
        var examId = await CreateExamAsync(admin, "Review exam", questions, TimeSpan.FromMinutes(-5));

        var email = UniqueEmail();
        var invite = await InviteAsync(admin, examId, email);
        var (candidate, _) = await factory.CandidateClientAsync(email);
        (await candidate.PostAsJsonAsync("/v1/invites/accept", new { code = CodeFromLink(invite.GetProperty("inviteLink").GetString()!) })).EnsureSuccessStatusCode();
        return (admin, candidate, examId);
    }

    private static async Task<Guid> StartAsync(HttpClient candidate, Guid examId) =>
        (await JsonAsync((await candidate.PostAsync($"/v1/me/exams/{examId}/attempts", content: null)).EnsureSuccessStatusCode())).GetProperty("id").GetGuid();

    /// <summary>Answers Q1 rightly and Q2 wrongly, leaves Q3 alone, and submits.</summary>
    private static async Task<JsonElement> SitAsync(HttpClient candidate, Guid examId)
    {
        var attemptId = await StartAsync(candidate, examId);
        var attempt = await candidate.GetFromJsonAsync<JsonElement>($"/v1/me/attempts/{attemptId}");
        foreach (var question in attempt.GetProperty("sections").EnumerateArray().SelectMany(s => s.GetProperty("questions").EnumerateArray()))
        {
            var text = question.GetProperty("text").GetString();
            var pick = text switch { "Q1" => "Right", "Q2" => "Wrong", _ => null };
            if (pick is null)
                continue;
            var option = question.GetProperty("options").EnumerateArray().Single(o => o.GetProperty("text").GetString() == pick).GetProperty("id").GetGuid();
            (await candidate.PutAsJsonAsync($"/v1/me/attempts/{attemptId}/answers/{question.GetProperty("id").GetGuid()}", new { optionId = option })).EnsureSuccessStatusCode();
        }

        return await JsonAsync((await candidate.PostAsync($"/v1/me/attempts/{attemptId}/submit", content: null)).EnsureSuccessStatusCode());
    }

    private static Task<HttpResponseMessage> ReviewAsync(HttpClient candidate, JsonElement attempt) =>
        candidate.GetAsync($"/v1/me/attempts/{attempt.GetProperty("id").GetGuid()}/review");

    private static JsonElement QuestionNamed(JsonElement review, string text) =>
        review.GetProperty("sections").EnumerateArray().SelectMany(s => s.GetProperty("questions").EnumerateArray()).Single(q => q.GetProperty("text").GetString() == text);

    [Fact]
    public async Task OnAnInstantExam_AFinishedAttempt_ShowsWhichAnswersWereRightAndWrong_WithTheCorrectOptions()
    {
        var (admin, candidate, examId) = await EnrolledAsync();
        using var _a = admin;
        using var _c = candidate;
        var submitted = await SitAsync(candidate, examId);
        Assert.True(submitted.GetProperty("review").GetProperty("available").GetBoolean());

        var review = await JsonAsync((await ReviewAsync(candidate, submitted)).EnsureSuccessStatusCode());

        Assert.Equal(1, review.GetProperty("correctCount").GetInt32());
        Assert.Equal(1, review.GetProperty("wrongCount").GetInt32());
        Assert.Equal(1, review.GetProperty("unansweredCount").GetInt32());
        Assert.Equal(submitted.GetProperty("score").GetDecimal(), review.GetProperty("score").GetDecimal());

        var right = QuestionNamed(review, "Q1");
        Assert.Equal("Correct", right.GetProperty("verdict").GetString());
        var rightChosen = right.GetProperty("options").EnumerateArray().Single(o => o.GetProperty("wasChosen").GetBoolean());
        Assert.Equal("Right", rightChosen.GetProperty("text").GetString());
        Assert.True(rightChosen.GetProperty("isCorrect").GetBoolean());

        var wrong = QuestionNamed(review, "Q2");
        Assert.Equal("Wrong", wrong.GetProperty("verdict").GetString());
        Assert.False(wrong.GetProperty("options").EnumerateArray().Single(o => o.GetProperty("wasChosen").GetBoolean()).GetProperty("isCorrect").GetBoolean());
        Assert.Equal("Right", wrong.GetProperty("options").EnumerateArray().Single(o => o.GetProperty("isCorrect").GetBoolean()).GetProperty("text").GetString());

        var skipped = QuestionNamed(review, "Q3");
        Assert.Equal("Unanswered", skipped.GetProperty("verdict").GetString());
        Assert.DoesNotContain(skipped.GetProperty("options").EnumerateArray(), o => o.GetProperty("wasChosen").GetBoolean());
    }

    [Fact]
    public async Task AnOpenAttempt_HasNoReview_AndNothingAShownWhileSittingCarriesTheKey()
    {
        var (admin, candidate, examId) = await EnrolledAsync();
        using var _a = admin;
        using var _c = candidate;
        var attemptId = await StartAsync(candidate, examId);

        await AssertProblemAsync(await candidate.GetAsync($"/v1/me/attempts/{attemptId}/review"), HttpStatusCode.Conflict, "attempt_not_submitted");

        var sitting = await (await candidate.GetAsync($"/v1/me/attempts/{attemptId}")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("isCorrect", sitting, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("wasChosen", sitting, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SomeoneElsesAttempt_HasNoReview_ForAnotherCandidateOrAnAdministrator()
    {
        var (admin, candidate, examId) = await EnrolledAsync();
        using var _a = admin;
        using var _c = candidate;
        var submitted = await SitAsync(candidate, examId);
        var (other, _) = await factory.CandidateClientAsync();
        using var _o = other;

        await AssertProblemAsync(await ReviewAsync(other, submitted), HttpStatusCode.NotFound, "attempt_not_found");
        await AssertProblemAsync(await ReviewAsync(admin, submitted), HttpStatusCode.NotFound, "attempt_not_found");
    }

    [Fact]
    public async Task BeforeAScheduledRelease_TheReviewIsRefused_AndTheAttemptSaysFromWhen()
    {
        var (admin, candidate, examId) = await EnrolledAsync();
        using var _a = admin;
        using var _c = candidate;
        var at = DateTime.UtcNow.AddDays(2);
        (await admin.PutAsJsonAsync($"/v1/exams/{examId}/result-release", new { mode = "Scheduled", releaseTime = at })).EnsureSuccessStatusCode();
        var submitted = await SitAsync(candidate, examId);

        var availability = submitted.GetProperty("review");
        Assert.False(availability.GetProperty("available").GetBoolean());
        Assert.Equal("Scheduled", availability.GetProperty("mode").GetString());
        Assert.Equal(at, availability.GetProperty("availableFromUtc").GetDateTime(), TimeSpan.FromSeconds(1));
        var refused = await ReviewAsync(candidate, submitted);
        await AssertProblemAsync(refused, HttpStatusCode.Conflict, "results_not_released");
    }

    [Fact]
    public async Task AManualExam_HoldsTheAnswersBack_UntilAnAdministratorReleasesThem()
    {
        var (admin, candidate, examId) = await EnrolledAsync();
        using var _a = admin;
        using var _c = candidate;
        (await admin.PutAsJsonAsync($"/v1/exams/{examId}/result-release", new { mode = "Manual" })).EnsureSuccessStatusCode();
        var submitted = await SitAsync(candidate, examId);
        await AssertProblemAsync(await ReviewAsync(candidate, submitted), HttpStatusCode.Conflict, "results_not_released");
        Assert.Equal(JsonValueKind.Null, submitted.GetProperty("review").GetProperty("availableFromUtc").ValueKind);

        (await admin.PostAsync($"/v1/exams/{examId}/results/release", content: null)).EnsureSuccessStatusCode();

        Assert.Equal(1, (await JsonAsync((await ReviewAsync(candidate, submitted)).EnsureSuccessStatusCode())).GetProperty("correctCount").GetInt32());
    }

    [Fact]
    public async Task TheAuthorsChoice_AppliesWhenTheReviewIsAskedFor_SoChangingItAffectsAttemptsAlreadyMade()
    {
        var (admin, candidate, examId) = await EnrolledAsync();
        using var _a = admin;
        using var _c = candidate;
        (await admin.PutAsJsonAsync($"/v1/exams/{examId}/result-release", new { mode = "Manual" })).EnsureSuccessStatusCode();
        var submitted = await SitAsync(candidate, examId);
        await AssertProblemAsync(await ReviewAsync(candidate, submitted), HttpStatusCode.Conflict, "results_not_released");

        (await admin.PutAsJsonAsync($"/v1/exams/{examId}/result-release", new { mode = "Instant" })).EnsureSuccessStatusCode();

        (await ReviewAsync(candidate, submitted)).EnsureSuccessStatusCode();
    }
}
