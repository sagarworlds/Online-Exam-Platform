using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// Editing a question (FR-5, FR-7) over real HTTP and a real database. Before anyone has answered it everything may change;
/// afterwards only the wording, so every stored score and every review stays right.
/// </summary>
public sealed class QuestionEditFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    /// <summary>Checks the problem's status and code and returns its body, which can only be read once.</summary>
    private static async Task<JsonElement> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var problem = await JsonAsync(response);
        Assert.Equal(code, problem.GetProperty("title").GetString());
        return problem;
    }

    private static Task<HttpResponseMessage> EditAsync(HttpClient admin, Guid id, string text, params object[] options) =>
        admin.PutAsJsonAsync($"/v1/questions/{id}", new { text, options });

    private static object Keep(JsonElement option, string? text = null, bool? isCorrect = null) =>
        new { id = option.GetProperty("id").GetGuid(), text = text ?? option.GetProperty("text").GetString(), isCorrect = isCorrect ?? option.GetProperty("isCorrect").GetBoolean() };

    private static async Task<JsonElement> GetAsync(HttpClient admin, Guid id) => await admin.GetFromJsonAsync<JsonElement>($"/v1/questions/{id}");

    [Fact]
    public async Task AnUnusedQuestion_CanBeChangedInEveryWay_AndOptionsThatStayKeepTheirIds()
    {
        using var admin = await factory.AdminClientAsync();
        var id = await CreateQuestionAsync(admin, "<p>Capital of France?</p>", "Paris", "Rome", "Oslo");
        var before = (await GetAsync(admin, id)).GetProperty("options").EnumerateArray().ToList();

        // Reorder, reword, change the answer, drop one option and add another.
        var response = await EditAsync(admin, id, "<p>Capital of <strong>Spain</strong>?</p>",
            new { text = "Madrid", isCorrect = true },
            Keep(before[1], text: "Rome (Italy)", isCorrect: false),
            Keep(before[0], isCorrect: false));

        var after = await JsonAsync(response.EnsureSuccessStatusCode());
        Assert.Equal("<p>Capital of <strong>Spain</strong>?</p>", after.GetProperty("text").GetString());
        var options = after.GetProperty("options").EnumerateArray().ToList();
        Assert.Equal(["Madrid", "Rome (Italy)", "Paris"], options.Select(o => o.GetProperty("text").GetString()));
        Assert.Equal([true, false, false], options.Select(o => o.GetProperty("isCorrect").GetBoolean()));
        Assert.Equal(before[1].GetProperty("id").GetGuid(), options[1].GetProperty("id").GetGuid());
        Assert.Equal(before[0].GetProperty("id").GetGuid(), options[2].GetProperty("id").GetGuid());
        Assert.DoesNotContain(options, o => o.GetProperty("id").GetGuid() == before[2].GetProperty("id").GetGuid()); // "Oslo" is gone

        // What was stored is what a fresh read says, not just what the edit answered with.
        var reread = await GetAsync(admin, id);
        Assert.Equal(after.GetProperty("options").ToString(), reread.GetProperty("options").ToString());
    }

    [Fact]
    public async Task TheEditIsSanitizedLikeACreate()
    {
        using var admin = await factory.AdminClientAsync();
        var id = await CreateQuestionAsync(admin, "Q?", "A", "B");
        var options = (await GetAsync(admin, id)).GetProperty("options").EnumerateArray().Select(o => Keep(o)).ToArray();

        var edited = await JsonAsync((await EditAsync(admin, id, "<p>Safe</p><script>alert(1)</script>", options)).EnsureSuccessStatusCode());

        Assert.Equal("<p>Safe</p>", edited.GetProperty("text").GetString());
    }

    [Fact]
    public async Task ABrokenEdit_Is400_AndTheQuestionIsLeftAsItWas()
    {
        using var admin = await factory.AdminClientAsync();
        var id = await CreateQuestionAsync(admin, "Original", "A", "B");
        var options = (await GetAsync(admin, id)).GetProperty("options").EnumerateArray().ToList();

        await AssertProblemAsync(await EditAsync(admin, id, "Changed", Keep(options[0]), Keep(options[1], text: "")), HttpStatusCode.BadRequest, "invalid_question");
        await AssertProblemAsync(await EditAsync(admin, id, "Changed", Keep(options[0], isCorrect: true), Keep(options[1], isCorrect: true)), HttpStatusCode.BadRequest, "invalid_question");
        await AssertProblemAsync(await EditAsync(admin, id, "", Keep(options[0]), Keep(options[1])), HttpStatusCode.BadRequest, "invalid_question");
        await AssertProblemAsync(await EditAsync(admin, id, "Changed", Keep(options[0])), HttpStatusCode.BadRequest, "invalid_question");

        Assert.Equal("Original", (await GetAsync(admin, id)).GetProperty("text").GetString());
    }

    [Fact]
    public async Task AnOptionIdFromAnotherQuestion_Is400()
    {
        using var admin = await factory.AdminClientAsync();
        var mine = await CreateQuestionAsync(admin, "Mine", "A", "B");
        var other = await CreateQuestionAsync(admin, "Other", "C", "D");
        var mineOptions = (await GetAsync(admin, mine)).GetProperty("options").EnumerateArray().ToList();
        var otherOptions = (await GetAsync(admin, other)).GetProperty("options").EnumerateArray().ToList();

        await AssertProblemAsync(await EditAsync(admin, mine, "Mine", Keep(mineOptions[0]), Keep(otherOptions[1])), HttpStatusCode.BadRequest, "invalid_question");
    }

    [Fact]
    public async Task AnUnknownQuestion_Is404()
    {
        using var admin = await factory.AdminClientAsync();

        await AssertProblemAsync(await EditAsync(admin, Guid.NewGuid(), "Q?", new { text = "A", isCorrect = true }, new { text = "B", isCorrect = false }), HttpStatusCode.NotFound, "question_not_found");
    }

    [Fact]
    public async Task EditingDoesNotMoveTheQuestion_OutOfItsChapter()
    {
        using var admin = await factory.AdminClientAsync();
        var book = await JsonAsync((await admin.PostAsJsonAsync("/v1/books", new { name = $"Book {Guid.NewGuid():N}" })).EnsureSuccessStatusCode());
        var withChapter = await JsonAsync((await admin.PostAsJsonAsync($"/v1/books/{book.GetProperty("id").GetGuid()}/chapters", new { title = "Ch 1" })).EnsureSuccessStatusCode());
        var chapterId = withChapter.GetProperty("chapters")[0].GetProperty("id").GetGuid();
        var created = await JsonAsync((await admin.PostAsJsonAsync("/v1/questions", new
        {
            text = "Filed",
            chapterId,
            options = new[] { new { text = "A", isCorrect = true }, new { text = "B", isCorrect = false } },
        })).EnsureSuccessStatusCode());
        var id = created.GetProperty("id").GetGuid();
        var options = created.GetProperty("options").EnumerateArray().Select(o => Keep(o)).ToArray();

        var edited = await JsonAsync((await EditAsync(admin, id, "Filed, reworded", options)).EnsureSuccessStatusCode());

        Assert.Equal(chapterId, edited.GetProperty("chapterId").GetGuid());
    }

    // ---- once a candidate has answered ------------------------------------------------------------

    /// <summary>A published, started exam of one question ("Capital?": "Right" correct, "Wrong"), and a candidate who has answered it rightly and submitted.</summary>
    private async Task<(HttpClient Admin, HttpClient Candidate, Guid QuestionId, JsonElement Submitted)> AnsweredAsync()
    {
        var admin = await factory.AdminClientAsync();
        var question = await CreateQuestionAsync(admin, "Capital?", "Right", "Wrong");
        var examId = await CreateExamAsync(admin, "Answered exam", [question], TimeSpan.FromMinutes(-5));
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);

        var sitting = await JsonAsync((await candidate.PostAsync($"/v1/me/exams/{examId}/attempts", content: null)).EnsureSuccessStatusCode());
        var shown = sitting.GetProperty("sections")[0].GetProperty("questions")[0];
        var right = shown.GetProperty("options").EnumerateArray().Single(o => o.GetProperty("text").GetString() == "Right").GetProperty("id").GetGuid();
        (await candidate.PutAsJsonAsync($"/v1/me/attempts/{sitting.GetProperty("id").GetGuid()}/answers/{shown.GetProperty("id").GetGuid()}", new { optionId = right })).EnsureSuccessStatusCode();
        var submitted = await JsonAsync((await candidate.PostAsync($"/v1/me/attempts/{sitting.GetProperty("id").GetGuid()}/submit", content: null)).EnsureSuccessStatusCode());

        return (admin, candidate, question, submitted);
    }

    [Fact]
    public async Task OnceAnswered_TheKeyAndTheOptionListAreLocked_WithAReasonThatSaysWhatToDoInstead()
    {
        var (admin, candidate, id, _) = await AnsweredAsync();
        using var _a = admin;
        using var _c = candidate;
        var options = (await GetAsync(admin, id)).GetProperty("options").EnumerateArray().ToList();

        var changedKey = await AssertProblemAsync(
            await EditAsync(admin, id, "Capital?", Keep(options[0], isCorrect: false), Keep(options[1], isCorrect: true)), HttpStatusCode.Conflict, "question_locked");
        Assert.Contains("create a new question", changedKey.GetProperty("detail").GetString());

        await AssertProblemAsync(await EditAsync(admin, id, "Capital?", Keep(options[0])), HttpStatusCode.BadRequest, "invalid_question"); // too few: refused before the lock matters
        await AssertProblemAsync(await EditAsync(admin, id, "Capital?", Keep(options[0]), Keep(options[1]), new { text = "Maybe", isCorrect = false }), HttpStatusCode.Conflict, "question_locked");
        await AssertProblemAsync(await EditAsync(admin, id, "Capital?", Keep(options[1]), Keep(options[0])), HttpStatusCode.Conflict, "question_locked");

        var unchanged = await GetAsync(admin, id);
        Assert.Equal("Right", unchanged.GetProperty("options").EnumerateArray().Single(o => o.GetProperty("isCorrect").GetBoolean()).GetProperty("text").GetString());
        Assert.Equal(2, unchanged.GetProperty("options").GetArrayLength());
    }

    [Fact]
    public async Task OnceAnswered_TheWordingCanStillBeCorrected_AndTheStoredScoreStillMatchesTheReview()
    {
        var (admin, candidate, id, submitted) = await AnsweredAsync();
        using var _a = admin;
        using var _c = candidate;
        var options = (await GetAsync(admin, id)).GetProperty("options").EnumerateArray().ToList();

        var edited = await JsonAsync((await EditAsync(admin, id, "Capital of France?", Keep(options[0], text: "Right (Paris)"), Keep(options[1], text: "Wrong (Rome)"))).EnsureSuccessStatusCode());

        Assert.Equal("Capital of France?", edited.GetProperty("text").GetString());
        Assert.True(edited.GetProperty("usage").GetProperty("answered").GetBoolean());

        // The candidate's review shows the corrected wording, but their mark and their right answer are exactly as they were.
        var review = await JsonAsync((await candidate.GetAsync($"/v1/me/attempts/{submitted.GetProperty("id").GetGuid()}/review")).EnsureSuccessStatusCode());
        Assert.Equal(submitted.GetProperty("score").GetDecimal(), review.GetProperty("score").GetDecimal());
        var reviewed = review.GetProperty("sections")[0].GetProperty("questions")[0];
        Assert.Equal("Capital of France?", reviewed.GetProperty("text").GetString());
        Assert.Equal("Correct", reviewed.GetProperty("verdict").GetString());
        var chosen = reviewed.GetProperty("options").EnumerateArray().Single(o => o.GetProperty("wasChosen").GetBoolean());
        Assert.Equal("Right (Paris)", chosen.GetProperty("text").GetString());
        Assert.True(chosen.GetProperty("isCorrect").GetBoolean());
    }
}
