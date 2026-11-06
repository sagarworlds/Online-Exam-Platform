using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ExamPlatform.IntegrationTests;

/// <summary>Adding several bank questions to an exam section in one call, over real HTTP and a real database.</summary>
public sealed class ExamBatchAddFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<Guid> CreateQuestionAsync(HttpClient admin, string text)
    {
        var response = await admin.PostAsJsonAsync("/v1/questions", new
        {
            text,
            options = new[] { new { text = "A", isCorrect = true }, new { text = "B", isCorrect = false } },
        });
        return (await JsonAsync(response.EnsureSuccessStatusCode())).GetProperty("id").GetGuid();
    }

    private static async Task<(Guid ExamId, Guid SectionId)> CreateExamAsync(HttpClient admin)
    {
        var exam = await JsonAsync((await admin.PostAsJsonAsync("/v1/exams", new { name = $"Batch {Guid.NewGuid().ToString("N")[..8]}" })).EnsureSuccessStatusCode());
        var examId = exam.GetProperty("id").GetGuid();
        var section = await JsonAsync((await admin.PostAsJsonAsync($"/v1/exams/{examId}/sections", new { name = "A" })).EnsureSuccessStatusCode());
        return (examId, section.GetProperty("id").GetGuid());
    }

    private static Task<HttpResponseMessage> AddManyAsync(HttpClient admin, Guid examId, Guid sectionId, params Guid[] questionIds) =>
        admin.PostAsJsonAsync($"/v1/exams/{examId}/sections/{sectionId}/questions/batch", new { questionIds });

    private static async Task<List<Guid>> SectionQuestionsAsync(HttpClient admin, Guid examId)
    {
        var exam = await admin.GetFromJsonAsync<JsonElement>($"/v1/exams/{examId}");
        return exam.GetProperty("sections")[0].GetProperty("questions").EnumerateArray().Select(q => q.GetProperty("questionId").GetGuid()).ToList();
    }

    [Fact]
    public async Task AddsEveryQuestionInTheOrderSent_AndTheExamKeepsThem()
    {
        using var admin = await factory.AdminClientAsync();
        var first = await CreateQuestionAsync(admin, "B1");
        var second = await CreateQuestionAsync(admin, "B2");
        var third = await CreateQuestionAsync(admin, "B3");
        var (examId, sectionId) = await CreateExamAsync(admin);

        var response = await AddManyAsync(admin, examId, sectionId, third, first, second);

        var added = (await JsonAsync(response.EnsureSuccessStatusCode())).EnumerateArray().ToList();
        Assert.Equal([third, first, second], added.Select(q => q.GetProperty("questionId").GetGuid()));
        Assert.Equal([1, 2, 3], added.Select(q => q.GetProperty("order").GetInt32()));
        Assert.Equal(["B3", "B1", "B2"], added.Select(q => q.GetProperty("text").GetString()));
        Assert.Equal([third, first, second], await SectionQuestionsAsync(admin, examId));
    }

    [Fact]
    public async Task OneBadQuestion_AddsNone_AndTheExamIsUnchanged()
    {
        using var admin = await factory.AdminClientAsync();
        var fine = await CreateQuestionAsync(admin, "F1");
        var (examId, sectionId) = await CreateExamAsync(admin);

        var response = await AddManyAsync(admin, examId, sectionId, fine, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await SectionQuestionsAsync(admin, examId));
    }

    [Fact]
    public async Task AQuestionAlreadyInTheExam_IsAConflict_AndAddsNone()
    {
        using var admin = await factory.AdminClientAsync();
        var held = await CreateQuestionAsync(admin, "H1");
        var fresh = await CreateQuestionAsync(admin, "H2");
        var (examId, sectionId) = await CreateExamAsync(admin);
        (await AddManyAsync(admin, examId, sectionId, held)).EnsureSuccessStatusCode();

        var response = await AddManyAsync(admin, examId, sectionId, fresh, held);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal([held], await SectionQuestionsAsync(admin, examId));
    }

    [Fact]
    public async Task AnEmptyList_IsRefused()
    {
        using var admin = await factory.AdminClientAsync();
        var (examId, sectionId) = await CreateExamAsync(admin);

        var response = await AddManyAsync(admin, examId, sectionId);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
