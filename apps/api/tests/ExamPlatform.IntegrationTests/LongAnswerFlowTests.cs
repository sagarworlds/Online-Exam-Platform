using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// An option, an accepted answer and a candidate's typed answer may each run to 10,000 characters (FR-5): they are stored whole, a
/// candidate can answer with the longest of them and is marked right, and one character more is refused.
/// </summary>
public sealed class LongAnswerFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const int Limit = 10_000;

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<(Guid AttemptId, JsonElement Question)> OpenAttemptAsync(HttpClient candidate, Guid examId)
    {
        var attemptId = (await JsonAsync((await candidate.PostAsJsonAsync($"/v1/me/exams/{examId}/attempts", new { instructionsAcknowledged = true })).EnsureSuccessStatusCode())).GetProperty("id").GetGuid();
        var attempt = await candidate.GetFromJsonAsync<JsonElement>($"/v1/me/attempts/{attemptId}");
        return (attemptId, attempt.GetProperty("sections")[0].GetProperty("questions")[0]);
    }

    [Fact]
    public async Task ALongestPossibleAcceptedAnswer_IsStored_AndACandidateWhoTypesItIsMarkedRight()
    {
        using var admin = await factory.AdminClientAsync();
        var answer = new string('x', Limit);
        var created = await admin.PostAsJsonAsync("/v1/questions", new { text = "Describe it.", isTextAnswer = true, acceptedAnswers = new[] { answer } });
        await ServerErrorLog.EnsureSuccessAsync(created);
        var questionId = (await JsonAsync(created)).GetProperty("id").GetGuid();
        var examId = await CreateExamAsync(admin, "Essay", [questionId], TimeSpan.FromMinutes(-5));
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;
        var (attemptId, question) = await OpenAttemptAsync(candidate, examId);

        (await candidate.PutAsJsonAsync($"/v1/me/attempts/{attemptId}/answers/{question.GetProperty("id").GetGuid()}", new { text = answer })).EnsureSuccessStatusCode();
        var submitted = await JsonAsync((await candidate.PostAsync($"/v1/me/attempts/{attemptId}/submit", content: null)).EnsureSuccessStatusCode());

        Assert.Equal(1m, submitted.GetProperty("score").GetDecimal());
        var stored = await admin.GetFromJsonAsync<JsonElement>($"/v1/questions/{questionId}");
        Assert.Equal(answer, Assert.Single(stored.GetProperty("acceptedAnswers").EnumerateArray()).GetString());
    }

    [Fact]
    public async Task ALongestPossibleOption_IsStoredWhole_AndACandidateWhoChoosesItIsMarkedRight()
    {
        using var admin = await factory.AdminClientAsync();
        var correct = new string('a', Limit);
        var questionId = await CreateQuestionAsync(admin, "Which is it?", correct, new string('b', Limit));
        var examId = await CreateExamAsync(admin, "Long options", [questionId], TimeSpan.FromMinutes(-5));
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;
        var (attemptId, question) = await OpenAttemptAsync(candidate, examId);

        var option = question.GetProperty("options").EnumerateArray().Single(o => o.GetProperty("text").GetString() == correct);
        (await candidate.PutAsJsonAsync($"/v1/me/attempts/{attemptId}/answers/{question.GetProperty("id").GetGuid()}", new { optionId = option.GetProperty("id").GetGuid() })).EnsureSuccessStatusCode();
        var submitted = await JsonAsync((await candidate.PostAsync($"/v1/me/attempts/{attemptId}/submit", content: null)).EnsureSuccessStatusCode());

        Assert.Equal(1m, submitted.GetProperty("score").GetDecimal());
    }

    [Fact]
    public async Task AnOptionOrAnAcceptedAnswerOneCharacterOverTheLimit_IsRefused()
    {
        using var admin = await factory.AdminClientAsync();
        var tooLong = new string('x', Limit + 1);

        var option = await admin.PostAsJsonAsync("/v1/questions", new
        {
            text = "Q?",
            options = new[] { new { text = "A", isCorrect = true }, new { text = tooLong, isCorrect = false } },
        });
        var answer = await admin.PostAsJsonAsync("/v1/questions", new { text = "Q?", isTextAnswer = true, acceptedAnswers = new[] { tooLong } });

        Assert.Equal(HttpStatusCode.BadRequest, option.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, answer.StatusCode);
    }

    [Fact]
    public async Task ATypedAnswerOneCharacterOverTheLimit_IsRefused()
    {
        using var admin = await factory.AdminClientAsync();
        var created = await admin.PostAsJsonAsync("/v1/questions", new { text = "Describe it.", isTextAnswer = true, acceptedAnswers = new[] { "short" } });
        await ServerErrorLog.EnsureSuccessAsync(created);
        var questionId = (await JsonAsync(created)).GetProperty("id").GetGuid();
        var examId = await CreateExamAsync(admin, "Essay limit", [questionId], TimeSpan.FromMinutes(-5));
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;
        var (attemptId, question) = await OpenAttemptAsync(candidate, examId);

        var response = await candidate.PutAsJsonAsync(
            $"/v1/me/attempts/{attemptId}/answers/{question.GetProperty("id").GetGuid()}", new { text = new string('x', Limit + 1) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
