using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExamPlatform.Modules.QuestionBank.Contracts;
using Microsoft.Extensions.DependencyInjection;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// Deleting a question (FR-5) over real HTTP and a real database: allowed for a question nothing uses, refused for one that is
/// in an exam or has been answered, so no exam and no result is ever left pointing at nothing.
/// </summary>
public sealed class QuestionDeleteFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<JsonElement> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var problem = await JsonAsync(response);
        Assert.Equal(code, problem.GetProperty("title").GetString());
        return problem;
    }

    private static async Task<bool> ExistsAsync(HttpClient admin, Guid id) => (await admin.GetAsync($"/v1/questions/{id}")).StatusCode == HttpStatusCode.OK;

    [Fact]
    public async Task AQuestionNothingUses_IsDeleted_AndIsGoneFromTheListTheReadAndTheBank()
    {
        using var admin = await factory.AdminClientAsync();
        var id = await CreateQuestionAsync(admin, "Delete me", "A", "B");

        var response = await admin.DeleteAsync($"/v1/questions/{id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await AssertProblemAsync(await admin.GetAsync($"/v1/questions/{id}"), HttpStatusCode.NotFound, "question_not_found");
        Assert.DoesNotContain((await admin.GetFromJsonAsync<JsonElement>("/v1/questions")).EnumerateArray(), q => q.GetProperty("id").GetGuid() == id);

        using var scope = factory.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<IQuestionBank>().GetAsync([id], CancellationToken.None));
    }

    [Fact]
    public async Task AQuestionInADraftExam_CannotBeDeleted_AndTheReasonNamesTheExam()
    {
        using var admin = await factory.AdminClientAsync();
        var id = await CreateQuestionAsync(admin, "Held by a draft", "A", "B");
        await CreateExamAsync(admin, "The draft holding it", [id], TimeSpan.FromHours(1), publish: false);

        var problem = await AssertProblemAsync(await admin.DeleteAsync($"/v1/questions/{id}"), HttpStatusCode.Conflict, "question_in_use");

        Assert.Contains("The draft holding it", problem.GetProperty("detail").GetString());
        Assert.True(await ExistsAsync(admin, id));
    }

    [Fact]
    public async Task AQuestionInAPublishedExam_CannotBeDeleted()
    {
        using var admin = await factory.AdminClientAsync();
        var id = await CreateQuestionAsync(admin, "Held by a published exam", "A", "B");
        await CreateExamAsync(admin, "Published, nobody started", [id], TimeSpan.FromMinutes(-5));

        await AssertProblemAsync(await admin.DeleteAsync($"/v1/questions/{id}"), HttpStatusCode.Conflict, "question_in_use");

        Assert.True(await ExistsAsync(admin, id));
    }

    [Fact]
    public async Task AQuestionACandidateHasAnswered_CannotBeDeleted_AndTheirReviewStillWorks()
    {
        using var admin = await factory.AdminClientAsync();
        var id = await CreateQuestionAsync(admin, "Answered", "Right", "Wrong");
        var examId = await CreateExamAsync(admin, "Sat exam", [id], TimeSpan.FromMinutes(-5));
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;
        var sitting = await JsonAsync((await candidate.PostAsync($"/v1/me/exams/{examId}/attempts", content: null)).EnsureSuccessStatusCode());
        var shown = sitting.GetProperty("sections")[0].GetProperty("questions")[0];
        var option = shown.GetProperty("options")[0].GetProperty("id").GetGuid();
        var attemptId = sitting.GetProperty("id").GetGuid();
        (await candidate.PutAsJsonAsync($"/v1/me/attempts/{attemptId}/answers/{shown.GetProperty("id").GetGuid()}", new { optionId = option })).EnsureSuccessStatusCode();
        (await candidate.PostAsync($"/v1/me/attempts/{attemptId}/submit", content: null)).EnsureSuccessStatusCode();

        await AssertProblemAsync(await admin.DeleteAsync($"/v1/questions/{id}"), HttpStatusCode.Conflict, "question_in_use");

        (await candidate.GetAsync($"/v1/me/attempts/{attemptId}/review")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task AnUnknownQuestion_Is404()
    {
        using var admin = await factory.AdminClientAsync();

        await AssertProblemAsync(await admin.DeleteAsync($"/v1/questions/{Guid.NewGuid()}"), HttpStatusCode.NotFound, "question_not_found");
    }
}
