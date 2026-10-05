using System.Net.Http.Json;
using System.Text.Json;
using ExamPlatform.Modules.QuestionBank.Contracts;
using Microsoft.Extensions.DependencyInjection;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// What the question list says about where a question is in use (FR-5, FR-7), over real HTTP and a real database. The edit
/// and delete rules rest on it: a question in an exam cannot be deleted, and one a candidate answered keeps its answer key.
/// </summary>
public sealed class QuestionUsageFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<JsonElement> UsageOfAsync(HttpClient admin, Guid questionId)
    {
        var listed = (await admin.GetFromJsonAsync<JsonElement>("/v1/questions")).EnumerateArray().Single(q => q.GetProperty("id").GetGuid() == questionId);
        var single = await admin.GetFromJsonAsync<JsonElement>($"/v1/questions/{questionId}");

        // The list and the single read come from the same reader, so they must always agree.
        Assert.Equal(listed.GetProperty("usage").ToString(), single.GetProperty("usage").ToString());
        return single.GetProperty("usage");
    }

    [Fact]
    public async Task AQuestionNothingUses_IsReportedUnused()
    {
        using var admin = await factory.AdminClientAsync();
        var question = await CreateQuestionAsync(admin, "Unused question", "Right", "Wrong");

        var usage = await UsageOfAsync(admin, question);

        Assert.Equal(0, usage.GetProperty("examCount").GetInt32());
        Assert.Empty(usage.GetProperty("examNames").EnumerateArray());
        Assert.False(usage.GetProperty("answered").GetBoolean());
    }

    [Fact]
    public async Task AQuestionInADraftExam_IsInUse_ButNotAnswered()
    {
        using var admin = await factory.AdminClientAsync();
        var question = await CreateQuestionAsync(admin, "Draft question", "Right", "Wrong");
        await CreateExamAsync(admin, "Draft holding it", [question], TimeSpan.FromHours(1), publish: false);

        var usage = await UsageOfAsync(admin, question);

        Assert.Equal(1, usage.GetProperty("examCount").GetInt32());
        Assert.Equal("Draft holding it", usage.GetProperty("examNames")[0].GetString());
        Assert.False(usage.GetProperty("answered").GetBoolean());
    }

    [Fact]
    public async Task OnceACandidateHasAnsweredIt_TheQuestionIsReportedAnswered()
    {
        using var admin = await factory.AdminClientAsync();
        var question = await CreateQuestionAsync(admin, "Answered question", "Right", "Wrong");
        var examId = await CreateExamAsync(admin, "Sat exam", [question], TimeSpan.FromMinutes(-5));
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;

        var attempt = await candidate.PostAsJsonAsync($"/v1/me/exams/{examId}/attempts", new { instructionsAcknowledged = true });
        var sitting = await attempt.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False((await UsageOfAsync(admin, question)).GetProperty("answered").GetBoolean()); // started, nothing saved yet

        var shown = sitting.GetProperty("sections")[0].GetProperty("questions")[0];
        var option = shown.GetProperty("options")[0].GetProperty("id").GetGuid();
        (await candidate.PutAsJsonAsync($"/v1/me/attempts/{sitting.GetProperty("id").GetGuid()}/answers/{shown.GetProperty("id").GetGuid()}", new { optionId = option })).EnsureSuccessStatusCode();

        var usage = await UsageOfAsync(admin, question);
        Assert.True(usage.GetProperty("answered").GetBoolean());
        Assert.Equal(1, usage.GetProperty("examCount").GetInt32());
    }

    [Fact]
    public async Task AMarkIsNotAnAnswer_AndTakingTheOnlyAnswerBackFreesTheQuestionAgain()
    {
        using var admin = await factory.AdminClientAsync();
        var question = await CreateQuestionAsync(admin, "Answered then cleared", "Right", "Wrong");
        var examId = await CreateExamAsync(admin, "Sat and cleared", [question], TimeSpan.FromMinutes(-5));
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;
        var sitting = await (await candidate.PostAsJsonAsync($"/v1/me/exams/{examId}/attempts", new { instructionsAcknowledged = true })).Content.ReadFromJsonAsync<JsonElement>();
        var attemptId = sitting.GetProperty("id").GetGuid();
        var shown = sitting.GetProperty("sections")[0].GetProperty("questions")[0];
        var questionId = shown.GetProperty("id").GetGuid();

        (await candidate.PutAsync($"/v1/me/attempts/{attemptId}/marks/{questionId}", content: null)).EnsureSuccessStatusCode();
        Assert.False((await UsageOfAsync(admin, question)).GetProperty("answered").GetBoolean()); // a note to oneself fixes nothing

        var option = shown.GetProperty("options")[0].GetProperty("id").GetGuid();
        (await candidate.PutAsJsonAsync($"/v1/me/attempts/{attemptId}/answers/{questionId}", new { optionId = option })).EnsureSuccessStatusCode();
        Assert.True((await UsageOfAsync(admin, question)).GetProperty("answered").GetBoolean());

        // No stored score depends on the answer key any more, so it may change again.
        (await candidate.DeleteAsync($"/v1/me/attempts/{attemptId}/answers/{questionId}")).EnsureSuccessStatusCode();
        Assert.False((await UsageOfAsync(admin, question)).GetProperty("answered").GetBoolean());
    }

    [Fact]
    public void BothModulesThatUseQuestions_HaveRegisteredAUsageSource_SoNoQuestionIsEverTakenForUnusedByOmission()
    {
        using var scope = factory.Services.CreateScope();

        var sources = scope.ServiceProvider.GetServices<IQuestionUsageSource>().Select(s => s.GetType().Name).Order().ToList();

        Assert.Equal(["AnsweredQuestionUsageSource", "ExamQuestionUsageSource"], sources);
    }
}
