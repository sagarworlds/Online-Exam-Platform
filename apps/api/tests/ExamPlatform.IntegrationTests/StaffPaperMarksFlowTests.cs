using System.Net.Http.Json;
using System.Text.Json;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>Staff opening the paper of an attempt see it marked: what the candidate chose, what was right, and the marks each question earned.</summary>
public sealed class StaffPaperMarksFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task ASubmittedAttempt_ShowsTheScore_AndEachQuestionsChoiceCorrectAnswerVerdictAndMarks()
    {
        using var admin = await factory.AdminClientAsync();
        var right = await CreateQuestionAsync(admin, "Paper right?", "Yes", "No");
        var wrong = await CreateQuestionAsync(admin, "Paper wrong?", "Yes", "No");
        var blank = await CreateQuestionAsync(admin, "Paper blank?", "Yes", "No");
        var examId = await CreateExamAsync(admin, "Marked paper", [right, wrong, blank], TimeSpan.FromMinutes(-5));
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;

        var started = await JsonAsync((await candidate.PostAsJsonAsync($"/v1/me/exams/{examId}/attempts", new { instructionsAcknowledged = true })).EnsureSuccessStatusCode());
        var attemptId = started.GetProperty("id").GetGuid();
        var questions = started.GetProperty("sections")[0].GetProperty("questions").EnumerateArray().ToList();
        Guid Option(string question, string text) =>
            questions.Single(q => q.GetProperty("text").GetString()!.Contains(question)).GetProperty("options").EnumerateArray()
                .Single(o => o.GetProperty("text").GetString() == text).GetProperty("id").GetGuid();
        Guid QuestionId(string question) => questions.Single(q => q.GetProperty("text").GetString()!.Contains(question)).GetProperty("id").GetGuid();
        (await candidate.PutAsJsonAsync($"/v1/me/attempts/{attemptId}/answers/{QuestionId("Paper right")}", new { optionId = Option("Paper right", "Yes") })).EnsureSuccessStatusCode();
        (await candidate.PutAsJsonAsync($"/v1/me/attempts/{attemptId}/answers/{QuestionId("Paper wrong")}", new { optionId = Option("Paper wrong", "No") })).EnsureSuccessStatusCode();
        var submitted = await JsonAsync((await candidate.PostAsync($"/v1/me/attempts/{attemptId}/submit", content: null)).EnsureSuccessStatusCode());

        var paper = await JsonAsync((await admin.GetAsync($"/v1/exams/{examId}/attempts/{attemptId}/paper")).EnsureSuccessStatusCode());

        Assert.Equal("Submitted", paper.GetProperty("status").GetString());
        Assert.Equal(submitted.GetProperty("score").GetDecimal(), paper.GetProperty("score").GetDecimal());
        Assert.Equal(submitted.GetProperty("maxScore").GetDecimal(), paper.GetProperty("maxScore").GetDecimal());
        var byText = paper.GetProperty("sections")[0].GetProperty("questions").EnumerateArray()
            .ToDictionary(q => q.GetProperty("text").GetString()!.Contains("Paper right") ? "right" : q.GetProperty("text").GetString()!.Contains("Paper wrong") ? "wrong" : "blank");
        Assert.Equal("Correct", byText["right"].GetProperty("verdict").GetString());
        Assert.Equal("Wrong", byText["wrong"].GetProperty("verdict").GetString());
        Assert.Equal("Unanswered", byText["blank"].GetProperty("verdict").GetString());
        Assert.True(byText["right"].GetProperty("marks").GetDecimal() > 0);

        var wrongOptions = byText["wrong"].GetProperty("options").EnumerateArray().ToList();
        Assert.Equal("Yes", Assert.Single(wrongOptions, o => o.GetProperty("isCorrect").GetBoolean()).GetProperty("text").GetString());
        Assert.Equal("No", Assert.Single(wrongOptions, o => o.GetProperty("wasChosen").GetBoolean()).GetProperty("text").GetString());
        Assert.DoesNotContain(byText["blank"].GetProperty("options").EnumerateArray(), o => o.GetProperty("wasChosen").GetBoolean());
    }

    [Fact]
    public async Task AnAttemptInProgress_ShowsTheChoicesSoFar_ButNoVerdictsOrScore()
    {
        using var admin = await factory.AdminClientAsync();
        var question = await CreateQuestionAsync(admin, "Open paper?", "Yes", "No");
        var examId = await CreateExamAsync(admin, "Open paper", [question], TimeSpan.FromMinutes(-5));
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;
        var started = await JsonAsync((await candidate.PostAsJsonAsync($"/v1/me/exams/{examId}/attempts", new { instructionsAcknowledged = true })).EnsureSuccessStatusCode());
        var attemptId = started.GetProperty("id").GetGuid();
        var q = started.GetProperty("sections")[0].GetProperty("questions")[0];
        var yes = q.GetProperty("options").EnumerateArray().Single(o => o.GetProperty("text").GetString() == "Yes").GetProperty("id").GetGuid();
        (await candidate.PutAsJsonAsync($"/v1/me/attempts/{attemptId}/answers/{q.GetProperty("id").GetGuid()}", new { optionId = yes })).EnsureSuccessStatusCode();

        var paper = await JsonAsync((await admin.GetAsync($"/v1/exams/{examId}/attempts/{attemptId}/paper")).EnsureSuccessStatusCode());

        Assert.Equal("InProgress", paper.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, paper.GetProperty("score").ValueKind);
        var shown = paper.GetProperty("sections")[0].GetProperty("questions")[0];
        Assert.Equal(JsonValueKind.Null, shown.GetProperty("verdict").ValueKind);
        Assert.Contains(shown.GetProperty("options").EnumerateArray(), o => o.GetProperty("wasChosen").GetBoolean() && o.GetProperty("text").GetString() == "Yes");
    }
}
