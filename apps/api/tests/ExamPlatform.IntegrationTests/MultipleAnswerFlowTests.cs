using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// Multiple-answer questions over real HTTP and a real database: an author marks several options correct, a candidate chooses a set,
/// and the question counts as right only when the set is exactly the correct options.
/// </summary>
public sealed class MultipleAnswerFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await JsonAsync(response)).GetProperty("title").GetString());
    }

    /// <summary>A multiple-answer question: "2" and "3" are correct, "4" and "6" are not.</summary>
    private static async Task<Guid> CreateMultiAsync(HttpClient admin)
    {
        var response = await admin.PostAsJsonAsync("/v1/questions", new
        {
            text = "Which are prime?",
            allowsMultiple = true,
            options = new[]
            {
                new { text = "2", isCorrect = true },
                new { text = "3", isCorrect = true },
                new { text = "4", isCorrect = false },
                new { text = "6", isCorrect = false },
            },
        });
        return (await JsonAsync(response.EnsureSuccessStatusCode())).GetProperty("id").GetGuid();
    }

    private async Task<(HttpClient Admin, HttpClient Candidate, Guid ExamId)> EnrolledWithAsync(params Guid[] questions)
    {
        var admin = await factory.AdminClientAsync();
        var examId = await CreateExamAsync(admin, "Multiple answers exam", questions, TimeSpan.FromMinutes(-5));
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        return (admin, candidate, examId);
    }

    private static async Task<JsonElement> StartAsync(HttpClient candidate, Guid examId) =>
        await JsonAsync((await candidate.PostAsync($"/v1/me/exams/{examId}/attempts", content: null)).EnsureSuccessStatusCode());

    private static Guid OptionId(JsonElement question, string text) =>
        question.GetProperty("options").EnumerateArray().Single(o => o.GetProperty("text").GetString() == text).GetProperty("id").GetGuid();

    private static Task<HttpResponseMessage> SaveAsync(HttpClient candidate, JsonElement attempt, JsonElement question, object body) =>
        candidate.PutAsJsonAsync($"/v1/me/attempts/{attempt.GetProperty("id").GetGuid()}/answers/{question.GetProperty("id").GetGuid()}", body);

    private static async Task<JsonElement> SubmitAsync(HttpClient candidate, JsonElement attempt) =>
        await JsonAsync((await candidate.PostAsync($"/v1/me/attempts/{attempt.GetProperty("id").GetGuid()}/submit", content: null)).EnsureSuccessStatusCode());

    [Fact]
    public async Task TheAuthorSeesTheQuestionAsMultipleAnswer_WithBothCorrectOptions()
    {
        using var admin = await factory.AdminClientAsync();
        var id = await CreateMultiAsync(admin);

        var question = await admin.GetFromJsonAsync<JsonElement>($"/v1/questions/{id}");

        Assert.True(question.GetProperty("allowsMultiple").GetBoolean());
        Assert.Equal(2, question.GetProperty("options").EnumerateArray().Count(o => o.GetProperty("isCorrect").GetBoolean()));
    }

    [Fact]
    public async Task ChoosingExactlyTheCorrectOptions_ScoresTheQuestion_AndTheCandidateIsShownTheirSet()
    {
        var admin = await factory.AdminClientAsync();
        var multi = await CreateMultiAsync(admin);
        var (_, candidate, examId) = await EnrolledWithAsync(multi);
        using var _a = admin;
        using var _c = candidate;
        var attempt = await StartAsync(candidate, examId);
        var question = attempt.GetProperty("sections")[0].GetProperty("questions")[0];
        Assert.True(question.GetProperty("allowsMultiple").GetBoolean());

        (await SaveAsync(candidate, attempt, question, new { optionIds = new[] { OptionId(question, "2"), OptionId(question, "3") } })).EnsureSuccessStatusCode();

        var resumed = await StartAsync(candidate, examId);
        var saved = resumed.GetProperty("sections")[0].GetProperty("questions")[0].GetProperty("selectedOptionIds").EnumerateArray().Select(i => i.GetGuid()).ToHashSet();
        Assert.True(saved.SetEquals([OptionId(question, "2"), OptionId(question, "3")]));
        var submitted = await SubmitAsync(candidate, attempt);
        Assert.Equal(1m, submitted.GetProperty("score").GetDecimal());
    }

    [Fact]
    public async Task ChoosingOnlySomeOfTheCorrectOptions_IsWrong()
    {
        var admin = await factory.AdminClientAsync();
        var multi = await CreateMultiAsync(admin);
        var (_, candidate, examId) = await EnrolledWithAsync(multi);
        using var _a = admin;
        using var _c = candidate;
        var attempt = await StartAsync(candidate, examId);
        var question = attempt.GetProperty("sections")[0].GetProperty("questions")[0];

        (await SaveAsync(candidate, attempt, question, new { optionIds = new[] { OptionId(question, "2") } })).EnsureSuccessStatusCode();

        Assert.Equal(0m, (await SubmitAsync(candidate, attempt)).GetProperty("score").GetDecimal());
    }

    [Fact]
    public async Task ChoosingTheCorrectOptionsAndAWrongOne_IsWrong_AndTheReviewShowsTheSetChosen()
    {
        var admin = await factory.AdminClientAsync();
        var multi = await CreateMultiAsync(admin);
        var (_, candidate, examId) = await EnrolledWithAsync(multi);
        using var _a = admin;
        using var _c = candidate;
        var attempt = await StartAsync(candidate, examId);
        var question = attempt.GetProperty("sections")[0].GetProperty("questions")[0];
        (await SaveAsync(candidate, attempt, question, new { optionIds = new[] { OptionId(question, "2"), OptionId(question, "3"), OptionId(question, "4") } })).EnsureSuccessStatusCode();

        var submitted = await SubmitAsync(candidate, attempt);
        var review = await JsonAsync((await candidate.GetAsync($"/v1/me/attempts/{attempt.GetProperty("id").GetGuid()}/review")).EnsureSuccessStatusCode());

        Assert.Equal(0m, submitted.GetProperty("score").GetDecimal());
        var reviewed = review.GetProperty("sections")[0].GetProperty("questions")[0];
        Assert.True(reviewed.GetProperty("allowsMultiple").GetBoolean());
        Assert.Equal("Wrong", reviewed.GetProperty("verdict").GetString());
        Assert.Equal(3, reviewed.GetProperty("options").EnumerateArray().Count(o => o.GetProperty("wasChosen").GetBoolean()));
    }

    [Fact]
    public async Task TheOldSingleOptionShape_StillWorks_ForASingleAnswerQuestion()
    {
        var admin = await factory.AdminClientAsync();
        var single = await CreateQuestionAsync(admin, "Single answer", "Right", "Wrong");
        var (_, candidate, examId) = await EnrolledWithAsync(single);
        using var _a = admin;
        using var _c = candidate;
        var attempt = await StartAsync(candidate, examId);
        var question = attempt.GetProperty("sections")[0].GetProperty("questions")[0];
        Assert.False(question.GetProperty("allowsMultiple").GetBoolean());

        (await SaveAsync(candidate, attempt, question, new { optionId = OptionId(question, "Right") })).EnsureSuccessStatusCode();

        var resumed = (await StartAsync(candidate, examId)).GetProperty("sections")[0].GetProperty("questions")[0];
        Assert.Equal(OptionId(question, "Right"), resumed.GetProperty("selectedOptionId").GetGuid());
        Assert.Equal(1m, (await SubmitAsync(candidate, attempt)).GetProperty("score").GetDecimal());
    }

    [Fact]
    public async Task SeveralOptions_ForASingleAnswerQuestion_Are400()
    {
        var admin = await factory.AdminClientAsync();
        var single = await CreateQuestionAsync(admin, "Single answer", "Right", "Wrong");
        var (_, candidate, examId) = await EnrolledWithAsync(single);
        using var _a = admin;
        using var _c = candidate;
        var attempt = await StartAsync(candidate, examId);
        var question = attempt.GetProperty("sections")[0].GetProperty("questions")[0];

        var response = await SaveAsync(candidate, attempt, question, new { optionIds = new[] { OptionId(question, "Right"), OptionId(question, "Wrong") } });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "invalid_answer");
    }

    [Fact]
    public async Task AnAnswerWithNoOptions_Is400_AndClearingStillTakesAnAnswerBack()
    {
        var admin = await factory.AdminClientAsync();
        var multi = await CreateMultiAsync(admin);
        var (_, candidate, examId) = await EnrolledWithAsync(multi);
        using var _a = admin;
        using var _c = candidate;
        var attempt = await StartAsync(candidate, examId);
        var question = attempt.GetProperty("sections")[0].GetProperty("questions")[0];

        await AssertProblemAsync(await SaveAsync(candidate, attempt, question, new { optionIds = Array.Empty<Guid>() }), HttpStatusCode.BadRequest, "invalid_answer");

        (await SaveAsync(candidate, attempt, question, new { optionIds = new[] { OptionId(question, "2") } })).EnsureSuccessStatusCode();
        var cleared = await candidate.DeleteAsync($"/v1/me/attempts/{attempt.GetProperty("id").GetGuid()}/answers/{question.GetProperty("id").GetGuid()}");
        Assert.Equal(HttpStatusCode.NoContent, cleared.StatusCode);
    }

    [Fact]
    public async Task AMultipleAnswerQuestionNeedsAtLeastOneCorrectOption_AndLeavesOneIncorrect()
    {
        using var admin = await factory.AdminClientAsync();
        object Body(params bool[] correct) => new
        {
            text = "Q?",
            allowsMultiple = true,
            options = correct.Select((c, i) => new { text = $"Option {i}", isCorrect = c }).ToArray(),
        };

        await AssertProblemAsync(await admin.PostAsJsonAsync("/v1/questions", Body(false, false)), HttpStatusCode.BadRequest, "invalid_question");
        await AssertProblemAsync(await admin.PostAsJsonAsync("/v1/questions", Body(true, true)), HttpStatusCode.BadRequest, "invalid_question");
        (await admin.PostAsJsonAsync("/v1/questions", Body(true, false))).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task OnceAnswered_TheQuestionCannotBeSwitchedBetweenSingleAndMultiple()
    {
        var admin = await factory.AdminClientAsync();
        var multi = await CreateMultiAsync(admin);
        var (_, candidate, examId) = await EnrolledWithAsync(multi);
        using var _a = admin;
        using var _c = candidate;
        var attempt = await StartAsync(candidate, examId);
        var question = attempt.GetProperty("sections")[0].GetProperty("questions")[0];
        (await SaveAsync(candidate, attempt, question, new { optionIds = new[] { OptionId(question, "2") } })).EnsureSuccessStatusCode();
        var stored = await admin.GetFromJsonAsync<JsonElement>($"/v1/questions/{multi}");
        var options = stored.GetProperty("options").EnumerateArray()
            .Select((o, i) => new { id = o.GetProperty("id").GetGuid(), text = o.GetProperty("text").GetString(), isCorrect = i == 0 })
            .ToArray();

        var response = await admin.PutAsJsonAsync($"/v1/questions/{multi}", new { text = "Which are prime?", options, allowsMultiple = false });

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "question_locked");
    }
}
