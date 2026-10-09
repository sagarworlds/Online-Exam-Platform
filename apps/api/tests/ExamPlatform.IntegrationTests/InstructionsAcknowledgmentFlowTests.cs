using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExamPlatform.Modules.ExamRuntime.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// A candidate must read and acknowledge the instructions before a new attempt begins, and the server holds them to it
/// whatever the page does (FR-17).
/// </summary>
public sealed class InstructionsAcknowledgmentFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<(HttpClient Candidate, Guid ExamId)> EnrolledCandidateAsync()
    {
        var admin = await factory.AdminClientAsync();
        var question = await CreateQuestionAsync(admin, "What is 2 + 2?", "4", "5");
        var examId = await CreateExamAsync(admin, "Instructions Exam", [question], startsIn: TimeSpan.FromMinutes(-5));
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        return (candidate, examId);
    }

    private static async Task AssertNotAcknowledgedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("instructions_not_acknowledged", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
    }

    private static async Task<JsonElement> MyExamAsync(HttpClient candidate, Guid examId) =>
        (await candidate.GetFromJsonAsync<JsonElement>("/v1/me/exams")).EnumerateArray().Single(e => e.GetProperty("examId").GetGuid() == examId);

    [Fact]
    public async Task Starting_WithNoBody_IsRefused_AndNoAttemptIsCreated()
    {
        var (candidate, examId) = await EnrolledCandidateAsync();

        await AssertNotAcknowledgedAsync(await candidate.PostAsync($"/v1/me/exams/{examId}/attempts", content: null));

        Assert.Equal(0, (await MyExamAsync(candidate, examId)).GetProperty("attemptsUsed").GetInt32());
    }

    [Fact]
    public async Task Starting_WithTheInstructionsNotAcknowledged_IsRefused()
    {
        var (candidate, examId) = await EnrolledCandidateAsync();

        await AssertNotAcknowledgedAsync(
            await candidate.PostAsJsonAsync($"/v1/me/exams/{examId}/attempts", new { instructionsAcknowledged = false }));
    }

    [Fact]
    public async Task Starting_WithTheInstructionsAcknowledged_BeginsTheAttempt_AndRecordsWhen()
    {
        var (candidate, examId) = await EnrolledCandidateAsync();

        var response = await candidate.PostAsJsonAsync($"/v1/me/exams/{examId}/attempts", new { instructionsAcknowledged = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var attemptId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var scope = factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<ExamRuntimeDbContext>().Attempts.AsNoTracking().SingleAsync(a => a.Id == attemptId);
        Assert.NotNull(stored.InstructionsAcknowledgedAtUtc);
        Assert.Equal(stored.StartedAtUtc, stored.InstructionsAcknowledgedAtUtc);
    }

    [Fact]
    public async Task ResumingAnOpenAttempt_NeedsNoAcknowledgment()
    {
        var (candidate, examId) = await EnrolledCandidateAsync();
        var started = await candidate.PostAsJsonAsync($"/v1/me/exams/{examId}/attempts", new { instructionsAcknowledged = true });
        var attemptId = (await started.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var resumed = await candidate.PostAsync($"/v1/me/exams/{examId}/attempts", content: null);

        Assert.Equal(HttpStatusCode.OK, resumed.StatusCode);
        Assert.Equal(attemptId, (await resumed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task MyExams_CarriesTheRulesTheInstructionsPageShows()
    {
        var (candidate, examId) = await EnrolledCandidateAsync();

        var rules = (await MyExamAsync(candidate, examId)).GetProperty("rules");

        Assert.Equal(1m, rules.GetProperty("correctMarks").GetDecimal());
        Assert.Equal(0m, rules.GetProperty("incorrectMarks").GetDecimal());
        Assert.Equal(0m, rules.GetProperty("unattemptedMarks").GetDecimal());
        Assert.False(rules.GetProperty("sectionLock").GetBoolean());
        Assert.False(rules.GetProperty("partialCredit").GetBoolean());
        Assert.Equal(1, rules.GetProperty("sectionCount").GetInt32());
        Assert.True(rules.GetProperty("contentProtection").GetBoolean());
    }
}
