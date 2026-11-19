using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExamPlatform.Modules.ExamRuntime.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// A saved or cleared answer loads the attempt with only the one question's answer and sequence number, not the whole attempt. These
/// are the cases where reading less could go wrong: closing an attempt whose time has run out must still score every answer, and the
/// sequence numbers (NFR-4) must still be checked against the ones held.
/// </summary>
public sealed class SlimAnswerLoadFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record Sitting(HttpClient Admin, HttpClient Candidate, Guid AttemptId, IReadOnlyList<(Guid Question, Guid Right, Guid Wrong)> Questions) : IDisposable
    {
        public void Dispose()
        {
            Admin.Dispose();
            Candidate.Dispose();
        }

        public string AnswerUrl(Guid question) => $"/v1/me/attempts/{AttemptId}/answers/{question}";
    }

    /// <summary>A candidate who has started an attempt at a published exam of <paramref name="count"/> two-option questions.</summary>
    private async Task<Sitting> StartedAsync(int count)
    {
        var admin = await factory.AdminClientAsync();
        var ids = new List<Guid>();
        for (var i = 1; i <= count; i++)
        {
            ids.Add(await CreateQuestionAsync(admin, $"Question {i}", "Right", "Wrong"));
        }

        var examId = await CreateExamAsync(admin, "Slim load exam", ids, TimeSpan.FromMinutes(-5), durationMinutes: 60);
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        var start = await candidate.PostAsJsonAsync($"/v1/me/exams/{examId}/attempts", new { instructionsAcknowledged = true });
        start.EnsureSuccessStatusCode();
        var body = await start.Content.ReadFromJsonAsync<JsonElement>();

        var questions = body.GetProperty("sections").EnumerateArray()
            .SelectMany(s => s.GetProperty("questions").EnumerateArray())
            .Select(q =>
            {
                var options = q.GetProperty("options").EnumerateArray().ToList();
                return (
                    q.GetProperty("id").GetGuid(),
                    options.Single(o => o.GetProperty("text").GetString() == "Right").GetProperty("id").GetGuid(),
                    options.Single(o => o.GetProperty("text").GetString() == "Wrong").GetProperty("id").GetGuid());
            })
            .ToList();
        return new Sitting(admin, candidate, body.GetProperty("id").GetGuid(), questions);
    }

    private static Task<HttpResponseMessage> SaveAsync(Sitting sitting, int question, bool right, long? sequence = null) =>
        sitting.Candidate.PutAsJsonAsync(
            sitting.AnswerUrl(sitting.Questions[question].Question),
            new { optionId = right ? sitting.Questions[question].Right : sitting.Questions[question].Wrong, sequence });

    /// <summary>Moves the attempt's deadline into the past, as if the candidate had walked away and the time had run out.</summary>
    private async Task ExpireAsync(Guid attemptId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExamRuntimeDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE \"examRuntime\".\"Attempts\" SET \"DeadlineUtc\" = {0} WHERE \"Id\" = {1}", DateTime.UtcNow.AddMinutes(-1), attemptId);
    }

    [Fact]
    public async Task ASaveAfterTheTimeHasRunOut_IsRefused_AndTheAttemptIsClosedWithEveryEarlierAnswerScored()
    {
        using var sitting = await StartedAsync(count: 3);
        (await SaveAsync(sitting, question: 0, right: true)).EnsureSuccessStatusCode();
        (await SaveAsync(sitting, question: 1, right: true)).EnsureSuccessStatusCode();
        (await SaveAsync(sitting, question: 2, right: false)).EnsureSuccessStatusCode();
        await ExpireAsync(sitting.AttemptId);

        // The late save is for the first question only, so only that question's answer is loaded with the attempt; closing must read the rest.
        var late = await SaveAsync(sitting, question: 0, right: false);

        Assert.False(late.IsSuccessStatusCode, "a save after the deadline must be refused");
        var result = await sitting.Candidate.GetFromJsonAsync<JsonElement>($"/v1/me/attempts/{sitting.AttemptId}/result");
        Assert.Equal(2, result.GetProperty("correctCount").GetInt32());
        Assert.Equal(1, result.GetProperty("wrongCount").GetInt32());
        Assert.Equal(0, result.GetProperty("unansweredCount").GetInt32());
    }

    [Fact]
    public async Task AFirstEverSaveAfterTheTimeHasRunOut_IsRefused_AndTheAttemptIsClosedWithEveryQuestionUnanswered()
    {
        using var sitting = await StartedAsync(count: 2);
        await ExpireAsync(sitting.AttemptId);

        var late = await SaveAsync(sitting, question: 0, right: true);

        Assert.False(late.IsSuccessStatusCode);
        var result = await sitting.Candidate.GetFromJsonAsync<JsonElement>($"/v1/me/attempts/{sitting.AttemptId}/result");
        Assert.Equal(0, result.GetProperty("correctCount").GetInt32());
        Assert.Equal(2, result.GetProperty("unansweredCount").GetInt32());
    }

    [Fact]
    public async Task ATypedAnswerSavedAfterTheTimeHasRunOut_IsRefused_AndTheEarlierTypedAndChosenAnswersStillCount()
    {
        var admin = await factory.AdminClientAsync();
        var typed = await admin.PostAsJsonAsync("/v1/questions", new { text = "Capital of France?", isTextAnswer = true, acceptedAnswers = new[] { "Paris" } });
        typed.EnsureSuccessStatusCode();
        var typedId = (await typed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var chosenId = await CreateQuestionAsync(admin, "Pick one", "Right", "Wrong");
        var examId = await CreateExamAsync(admin, "Typed expiry exam", [typedId, chosenId], TimeSpan.FromMinutes(-5), durationMinutes: 60);
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _a = admin;
        using var _c = candidate;
        var start = await candidate.PostAsJsonAsync($"/v1/me/exams/{examId}/attempts", new { instructionsAcknowledged = true });
        start.EnsureSuccessStatusCode();
        var body = await start.Content.ReadFromJsonAsync<JsonElement>();
        var attemptId = body.GetProperty("id").GetGuid();
        var chosen = body.GetProperty("sections").EnumerateArray().SelectMany(s => s.GetProperty("questions").EnumerateArray())
            .Single(q => q.GetProperty("id").GetGuid() == chosenId);
        var right = chosen.GetProperty("options").EnumerateArray().Single(o => o.GetProperty("text").GetString() == "Right").GetProperty("id").GetGuid();

        (await candidate.PutAsJsonAsync($"/v1/me/attempts/{attemptId}/answers/{typedId}", new { text = "Paris" })).EnsureSuccessStatusCode();
        (await candidate.PutAsJsonAsync($"/v1/me/attempts/{attemptId}/answers/{chosenId}", new { optionId = right })).EnsureSuccessStatusCode();
        await ExpireAsync(attemptId);

        var late = await candidate.PutAsJsonAsync($"/v1/me/attempts/{attemptId}/answers/{typedId}", new { text = "Lyon" });

        Assert.False(late.IsSuccessStatusCode);
        var result = await candidate.GetFromJsonAsync<JsonElement>($"/v1/me/attempts/{attemptId}/result");
        Assert.Equal(2, result.GetProperty("correctCount").GetInt32());
    }

    [Fact]
    public async Task AClearAfterTheTimeHasRunOut_IsRefused_AndTheAnswerItWouldHaveRemovedStillCounts()
    {
        using var sitting = await StartedAsync(count: 2);
        (await SaveAsync(sitting, question: 0, right: true)).EnsureSuccessStatusCode();
        (await SaveAsync(sitting, question: 1, right: true)).EnsureSuccessStatusCode();
        await ExpireAsync(sitting.AttemptId);

        var late = await sitting.Candidate.DeleteAsync(sitting.AnswerUrl(sitting.Questions[0].Question));

        Assert.False(late.IsSuccessStatusCode);
        var result = await sitting.Candidate.GetFromJsonAsync<JsonElement>($"/v1/me/attempts/{sitting.AttemptId}/result");
        Assert.Equal(2, result.GetProperty("correctCount").GetInt32());
    }

    [Fact]
    public async Task AnOlderSaveForAQuestion_IsStillRefused_WhateverTheOtherQuestionsHold()
    {
        using var sitting = await StartedAsync(count: 2);
        (await SaveAsync(sitting, question: 0, right: true, sequence: 5)).EnsureSuccessStatusCode();
        // A different question's sequence number must not be confused with this one's, nor an absent row with a held one.
        (await SaveAsync(sitting, question: 1, right: true, sequence: 1)).EnsureSuccessStatusCode();

        var stale = await SaveAsync(sitting, question: 0, right: false, sequence: 3);
        var fresh = await SaveAsync(sitting, question: 0, right: false, sequence: 6);

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(5, (await stale.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("currentSequence").GetInt64());
        Assert.Equal(HttpStatusCode.NoContent, fresh.StatusCode);
    }

    [Fact]
    public async Task SavingTheSameQuestionAgain_ReplacesItsAnswer_AndLeavesTheOthersAlone()
    {
        using var sitting = await StartedAsync(count: 3);
        (await SaveAsync(sitting, question: 0, right: false)).EnsureSuccessStatusCode();
        (await SaveAsync(sitting, question: 1, right: true)).EnsureSuccessStatusCode();
        (await SaveAsync(sitting, question: 0, right: true)).EnsureSuccessStatusCode();

        (await sitting.Candidate.PostAsync($"/v1/me/attempts/{sitting.AttemptId}/submit", content: null)).EnsureSuccessStatusCode();
        var result = await sitting.Candidate.GetFromJsonAsync<JsonElement>($"/v1/me/attempts/{sitting.AttemptId}/result");

        Assert.Equal(2, result.GetProperty("correctCount").GetInt32());
        Assert.Equal(0, result.GetProperty("wrongCount").GetInt32());
        Assert.Equal(1, result.GetProperty("unansweredCount").GetInt32());
    }
}
