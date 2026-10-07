using System.Net.Http.Json;
using System.Text.Json;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>FR-51: a candidate sees a question in the language they ask for, with the same options and the same marking.</summary>
public sealed class QuestionLanguageContentFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<JsonElement> ReadAttemptAsync(HttpClient candidate, Guid attemptId, string? acceptLanguage)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/v1/me/attempts/{attemptId}");
        if (acceptLanguage is not null)
            request.Headers.TryAddWithoutValidation("Accept-Language", acceptLanguage);
        return await JsonAsync((await candidate.SendAsync(request)).EnsureSuccessStatusCode());
    }

    private static JsonElement TheQuestion(JsonElement attempt) => attempt.GetProperty("sections")[0].GetProperty("questions")[0];

    // Sorted, because an attempt may shuffle options and these tests are about which words and ids are shown, not where.
    private static string[] OptionTexts(JsonElement question) =>
        question.GetProperty("options").EnumerateArray().Select(o => o.GetProperty("text").GetString()!).OrderBy(t => t, StringComparer.Ordinal).ToArray();

    private static Guid[] OptionIds(JsonElement question) =>
        question.GetProperty("options").EnumerateArray().Select(o => o.GetProperty("id").GetGuid()).Order().ToArray();

    [Fact]
    public async Task ACandidateSeesTheQuestionInTheLanguageTheyAskFor_WithTheSameOptionsAndTheSameMarking()
    {
        using var admin = await factory.AdminClientAsync();
        var source = await CreateQuestionAsync(admin, "Capital of Contentland?", "Contentville", "Other");
        (await admin.PostAsJsonAsync($"/v1/questions/{source}/translations", new
        {
            language = "hi",
            text = "<p>कंटेंटलैंड की राजधानी?</p>",
            options = new[] { "कंटेंटविल", "अन्य" },
        })).EnsureSuccessStatusCode();
        var examId = await CreateExamAsync(admin, "Language content exam", [source], TimeSpan.FromMinutes(-5));
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;

        var started = await JsonAsync((await candidate.PostAsJsonAsync($"/v1/me/exams/{examId}/attempts", new { instructionsAcknowledged = true })).EnsureSuccessStatusCode());
        var attemptId = started.GetProperty("id").GetGuid();
        var english = TheQuestion(await ReadAttemptAsync(candidate, attemptId, null));
        Assert.Contains("Capital of Contentland?", english.GetProperty("text").GetString());
        Assert.Equal(new[] { "Contentville", "Other" }, OptionTexts(english));

        // In Hindi: the words change, the options do not (same ids), so an answer saved from this screen is an answer to the same question.
        var hindi = TheQuestion(await ReadAttemptAsync(candidate, attemptId, "hi-IN,hi;q=0.9,en;q=0.5"));
        Assert.Contains("कंटेंटलैंड", hindi.GetProperty("text").GetString());
        Assert.Equal(OptionIds(english), OptionIds(hindi));
        Assert.Equal(new[] { "अन्य", "कंटेंटविल" }.OrderBy(t => t, StringComparer.Ordinal), OptionTexts(hindi));

        // A language with no translation, and English asked for first, both show the question as it was written.
        Assert.Contains("Capital of Contentland?", TheQuestion(await ReadAttemptAsync(candidate, attemptId, "mr")).GetProperty("text").GetString());
        Assert.Contains("Capital of Contentland?", TheQuestion(await ReadAttemptAsync(candidate, attemptId, "en,hi;q=0.5")).GetProperty("text").GetString());

        // Choosing "कंटेंटविल" on the Hindi screen is choosing the source's correct option.
        var correct = hindi.GetProperty("options").EnumerateArray().Single(o => o.GetProperty("text").GetString() == "कंटेंटविल").GetProperty("id").GetGuid();
        (await candidate.PutAsJsonAsync($"/v1/me/attempts/{attemptId}/answers/{source}", new { optionId = correct })).EnsureSuccessStatusCode();
        var submitted = await JsonAsync((await candidate.PostAsync($"/v1/me/attempts/{attemptId}/submit", content: null)).EnsureSuccessStatusCode());
        Assert.Equal(submitted.GetProperty("maxScore").GetDecimal(), submitted.GetProperty("score").GetDecimal());
        Assert.True(submitted.GetProperty("score").GetDecimal() > 0);
    }

    [Fact]
    public async Task ATranslationWithoutTheSameNumberOfOptions_IsNotShown()
    {
        using var admin = await factory.AdminClientAsync();
        var source = await CreateQuestionAsync(admin, "Option count question for candidates?", "Yes", "No");
        (await admin.PostAsJsonAsync($"/v1/questions/{source}/translations", new { language = "mr", text = "<p>होय की नाही?</p>", options = new[] { "होय", "नाही" } })).EnsureSuccessStatusCode();
        // The source gains an option after the translation was written, so the two no longer line up.
        var shown = await JsonAsync((await admin.GetAsync($"/v1/questions/{source}")).EnsureSuccessStatusCode());
        var options = shown.GetProperty("options").EnumerateArray()
            .Select(o => new { id = o.GetProperty("id").GetGuid(), text = o.GetProperty("text").GetString()!, isCorrect = o.GetProperty("isCorrect").GetBoolean() })
            .Append(new { id = Guid.Empty, text = "Maybe", isCorrect = false }).ToArray();
        (await admin.PutAsJsonAsync($"/v1/questions/{source}", new
        {
            text = shown.GetProperty("text").GetString(),
            options = options.Select(o => new { id = o.id == Guid.Empty ? (Guid?)null : o.id, o.text, o.isCorrect }).ToArray(),
        })).EnsureSuccessStatusCode();
        var examId = await CreateExamAsync(admin, "Mismatched translation exam", [source], TimeSpan.FromMinutes(-5));
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;
        var started = await JsonAsync((await candidate.PostAsJsonAsync($"/v1/me/exams/{examId}/attempts", new { instructionsAcknowledged = true })).EnsureSuccessStatusCode());

        var question = TheQuestion(await ReadAttemptAsync(candidate, started.GetProperty("id").GetGuid(), "mr"));

        Assert.Contains("Option count question for candidates?", question.GetProperty("text").GetString());
        Assert.Equal(3, OptionTexts(question).Length);
    }
}
