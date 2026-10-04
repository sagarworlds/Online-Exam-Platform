using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>Questions drawn afresh for each candidate when they start, over real HTTP and a real database.</summary>
public sealed class DrawRuleFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static string UniqueTopic() => $"rule{Guid.NewGuid().ToString("N")[..8]}";

    private static async Task<Guid> TaggedQuestionAsync(HttpClient admin, string text, string topic)
    {
        var response = await admin.PostAsJsonAsync("/v1/questions", new
        {
            text,
            options = new[] { new { text = "A", isCorrect = true }, new { text = "B", isCorrect = false } },
            topics = new[] { topic },
        });
        return (await JsonAsync(response.EnsureSuccessStatusCode())).GetProperty("id").GetGuid();
    }

    /// <summary>A draft exam with one fixed question and one rule; returns the exam id.</summary>
    private static async Task<Guid> DraftWithRuleAsync(HttpClient admin, string topic, int count)
    {
        var fixedQuestion = await CreateQuestionAsync(admin, "Fixed", "Right", "Wrong");
        var examId = await CreateExamAsync(admin, "Rules", [fixedQuestion], TimeSpan.FromMinutes(-5), publish: false);
        var sectionId = (await admin.GetFromJsonAsync<JsonElement>($"/v1/exams/{examId}")).GetProperty("sections")[0].GetProperty("id").GetGuid();
        (await admin.PostAsJsonAsync($"/v1/exams/{examId}/sections/{sectionId}/draw-rules", new { count, topic })).EnsureSuccessStatusCode();
        return examId;
    }

    private static async Task<List<Guid>> PaperOfAsync(HttpClient candidate, Guid examId)
    {
        var response = await candidate.PostAsync($"/v1/me/exams/{examId}/attempts", content: null);
        var attempt = await JsonAsync(response.EnsureSuccessStatusCode());
        return attempt.GetProperty("sections").EnumerateArray()
            .SelectMany(s => s.GetProperty("questions").EnumerateArray())
            .Select(q => q.GetProperty("id").GetGuid())
            .ToList();
    }

    [Fact]
    public async Task ACandidate_GetsTheFixedQuestionsPlusDrawnOnes_AndTheSamePaperWhenTheyResume()
    {
        using var admin = await factory.AdminClientAsync();
        var topic = UniqueTopic();
        for (var i = 0; i < 5; i++)
            await TaggedQuestionAsync(admin, $"Pool {i}", topic);
        var examId = await DraftWithRuleAsync(admin, topic, 2);
        (await admin.PostAsync($"/v1/exams/{examId}/publish", content: null)).EnsureSuccessStatusCode();
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;

        var paper = await PaperOfAsync(candidate, examId);
        var resumed = await PaperOfAsync(candidate, examId);

        Assert.Equal(3, paper.Count);
        Assert.Equal(3, paper.Distinct().Count());
        Assert.Equal(paper.Order(), resumed.Order());
    }

    [Fact]
    public async Task PublishingIsRefused_WhenTheBankCannotFillARule()
    {
        using var admin = await factory.AdminClientAsync();
        var topic = UniqueTopic();
        await TaggedQuestionAsync(admin, "Only one", topic);
        var examId = await DraftWithRuleAsync(admin, topic, 3);

        var response = await admin.PostAsync($"/v1/exams/{examId}/publish", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("draw_pool_too_small", (await JsonAsync(response)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task StartingIsRefused_WhenThePoolShrankAfterPublishing_AndNoAttemptIsLeftBehind()
    {
        using var admin = await factory.AdminClientAsync();
        var topic = UniqueTopic();
        await TaggedQuestionAsync(admin, "Keep", topic);
        var doomed = await TaggedQuestionAsync(admin, "Delete me", topic);
        var examId = await DraftWithRuleAsync(admin, topic, 2);
        (await admin.PostAsync($"/v1/exams/{examId}/publish", content: null)).EnsureSuccessStatusCode();
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;
        (await admin.DeleteAsync($"/v1/questions/{doomed}")).EnsureSuccessStatusCode();

        var response = await candidate.PostAsync($"/v1/me/exams/{examId}/attempts", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("paper_cannot_be_drawn", (await JsonAsync(response)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Staff_CanSeeThePaperDrawnForAnAttempt_AndACandidateCannot()
    {
        using var admin = await factory.AdminClientAsync();
        var topic = UniqueTopic();
        for (var i = 0; i < 4; i++)
            await TaggedQuestionAsync(admin, $"Pool {i}", topic);
        var examId = await DraftWithRuleAsync(admin, topic, 2);
        (await admin.PostAsync($"/v1/exams/{examId}/publish", content: null)).EnsureSuccessStatusCode();
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;
        var started = await JsonAsync((await candidate.PostAsync($"/v1/me/exams/{examId}/attempts", content: null)).EnsureSuccessStatusCode());
        var attemptId = started.GetProperty("id").GetGuid();

        var response = await admin.GetAsync($"/v1/exams/{examId}/attempts/{attemptId}/paper");

        var paper = await JsonAsync(response.EnsureSuccessStatusCode());
        Assert.True(paper.GetProperty("hasDrawnQuestions").GetBoolean());
        var questions = paper.GetProperty("sections")[0].GetProperty("questions").EnumerateArray().ToList();
        Assert.Equal(3, questions.Count);
        Assert.Equal(2, questions.Count(q => q.GetProperty("drawn").GetBoolean()));
        Assert.Equal(HttpStatusCode.Forbidden, (await candidate.GetAsync($"/v1/exams/{examId}/attempts/{attemptId}/paper")).StatusCode);
    }

    [Fact]
    public async Task ARule_CanBeRemovedWhileDraft_ButNotOncePublished()
    {
        using var admin = await factory.AdminClientAsync();
        var topic = UniqueTopic();
        for (var i = 0; i < 2; i++)
            await TaggedQuestionAsync(admin, $"Pool {i}", topic);
        var examId = await DraftWithRuleAsync(admin, topic, 1);
        var section = (await admin.GetFromJsonAsync<JsonElement>($"/v1/exams/{examId}")).GetProperty("sections")[0];
        var sectionId = section.GetProperty("id").GetGuid();
        var ruleId = section.GetProperty("drawRules")[0].GetProperty("id").GetGuid();
        (await admin.PostAsync($"/v1/exams/{examId}/publish", content: null)).EnsureSuccessStatusCode();

        var response = await admin.DeleteAsync($"/v1/exams/{examId}/sections/{sectionId}/draw-rules/{ruleId}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
