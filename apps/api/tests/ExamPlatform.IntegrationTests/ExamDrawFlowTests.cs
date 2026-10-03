using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ExamPlatform.IntegrationTests;

/// <summary>Drawing random questions that match a difficulty or topic into an exam section, over real HTTP and a real database.</summary>
public sealed class ExamDrawFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<Guid> CreateQuestionAsync(HttpClient admin, string text, string? difficulty, string topic)
    {
        var response = await admin.PostAsJsonAsync("/v1/questions", new
        {
            text,
            options = new[] { new { text = "A", isCorrect = true }, new { text = "B", isCorrect = false } },
            difficulty,
            topics = new[] { topic },
        });
        return (await JsonAsync(response.EnsureSuccessStatusCode())).GetProperty("id").GetGuid();
    }

    private static async Task<(Guid ExamId, Guid SectionId)> CreateExamAsync(HttpClient admin)
    {
        var exam = await JsonAsync((await admin.PostAsJsonAsync("/v1/exams", new { name = $"Draw {Guid.NewGuid().ToString("N")[..8]}" })).EnsureSuccessStatusCode());
        var examId = exam.GetProperty("id").GetGuid();
        var section = await JsonAsync((await admin.PostAsJsonAsync($"/v1/exams/{examId}/sections", new { name = "A" })).EnsureSuccessStatusCode());
        return (examId, section.GetProperty("id").GetGuid());
    }

    private static Task<HttpResponseMessage> DrawAsync(HttpClient admin, Guid examId, Guid sectionId, object body) =>
        admin.PostAsJsonAsync($"/v1/exams/{examId}/sections/{sectionId}/questions/draw", body);

    private static async Task<List<Guid>> SectionQuestionsAsync(HttpClient admin, Guid examId)
    {
        var exam = await admin.GetFromJsonAsync<JsonElement>($"/v1/exams/{examId}");
        return exam.GetProperty("sections")[0].GetProperty("questions").EnumerateArray().Select(q => q.GetProperty("questionId").GetGuid()).ToList();
    }

    [Fact]
    public async Task DrawsOnlyQuestionsThatMatch_AndTheExamKeepsThem()
    {
        using var admin = await factory.AdminClientAsync();
        var topic = $"draw{Guid.NewGuid().ToString("N")[..8]}";
        var easy = new[] { await CreateQuestionAsync(admin, "E1", "easy", topic), await CreateQuestionAsync(admin, "E2", "easy", topic), await CreateQuestionAsync(admin, "E3", "easy", topic) };
        var hard = await CreateQuestionAsync(admin, "H1", "hard", topic);
        var (examId, sectionId) = await CreateExamAsync(admin);

        var response = await DrawAsync(admin, examId, sectionId, new { count = 2, difficulty = "easy", topic });

        var added = (await JsonAsync(response.EnsureSuccessStatusCode())).EnumerateArray().ToList();
        Assert.Equal(2, added.Count);
        var drawn = added.Select(q => q.GetProperty("questionId").GetGuid()).ToList();
        Assert.All(drawn, id => Assert.Contains(id, easy));
        Assert.DoesNotContain(hard, drawn);
        Assert.Equal([1, 2], added.Select(q => q.GetProperty("order").GetInt32()));
        Assert.All(added, q => Assert.StartsWith("E", q.GetProperty("text").GetString()));

        // The exam keeps a fixed list: what a fresh read says is what the draw returned.
        Assert.Equal(drawn, await SectionQuestionsAsync(admin, examId));
    }

    [Fact]
    public async Task ASecondDraw_NeverRepeatsWhatTheExamAlreadyHolds()
    {
        using var admin = await factory.AdminClientAsync();
        var topic = $"draw{Guid.NewGuid().ToString("N")[..8]}";
        for (var i = 0; i < 4; i++)
            await CreateQuestionAsync(admin, $"Q{i}", null, topic);
        var (examId, sectionId) = await CreateExamAsync(admin);

        (await DrawAsync(admin, examId, sectionId, new { count = 2, topic })).EnsureSuccessStatusCode();
        (await DrawAsync(admin, examId, sectionId, new { count = 2, topic })).EnsureSuccessStatusCode();

        var held = await SectionQuestionsAsync(admin, examId);
        Assert.Equal(4, held.Count);
        Assert.Equal(4, held.Distinct().Count());

        // All four are in; there is nothing left to draw.
        Assert.Equal(HttpStatusCode.Conflict, (await DrawAsync(admin, examId, sectionId, new { count = 1, topic })).StatusCode);
    }

    [Fact]
    public async Task WhenTooFewMatch_NothingIsAdded_AndTheAnswerIs409()
    {
        using var admin = await factory.AdminClientAsync();
        var topic = $"draw{Guid.NewGuid().ToString("N")[..8]}";
        await CreateQuestionAsync(admin, "Only", "medium", topic);
        var (examId, sectionId) = await CreateExamAsync(admin);

        var response = await DrawAsync(admin, examId, sectionId, new { count = 3, topic });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("not_enough_questions", (await JsonAsync(response)).GetProperty("title").GetString());
        Assert.Empty(await SectionQuestionsAsync(admin, examId));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task ACountOutsideTheRange_Is400(int count)
    {
        using var admin = await factory.AdminClientAsync();
        var (examId, sectionId) = await CreateExamAsync(admin);

        Assert.Equal(HttpStatusCode.BadRequest, (await DrawAsync(admin, examId, sectionId, new { count })).StatusCode);
    }

    [Fact]
    public async Task ADifficultyThatIsNotALevel_Is400()
    {
        using var admin = await factory.AdminClientAsync();
        var (examId, sectionId) = await CreateExamAsync(admin);

        Assert.Equal(HttpStatusCode.BadRequest, (await DrawAsync(admin, examId, sectionId, new { count = 1, difficulty = "tricky" })).StatusCode);
    }

    [Fact]
    public async Task ForAnUnknownSection_Is404()
    {
        using var admin = await factory.AdminClientAsync();
        var (examId, _) = await CreateExamAsync(admin);

        Assert.Equal(HttpStatusCode.NotFound, (await DrawAsync(admin, examId, Guid.NewGuid(), new { count = 1 })).StatusCode);
    }
}
