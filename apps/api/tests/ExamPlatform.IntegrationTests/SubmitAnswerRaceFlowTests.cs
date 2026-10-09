using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// An answer saved at the same moment as the submit is either scored or refused, never lost. The page saves each answer as it is chosen
/// and does not wait for them, so a candidate who answers and submits in the same instant used to get a score that missed the answer.
/// </summary>
public sealed class SubmitAnswerRaceFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const int Questions = 10;

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task AnswersSavedWhileTheAttemptIsSubmitted_AreAllScored_OrRefused_AndTheScoreAlwaysMatchesTheStoredAnswers()
    {
        using var admin = await factory.AdminClientAsync();
        var ids = new List<Guid>();
        for (var i = 0; i < Questions; i++)
            ids.Add(await CreateQuestionAsync(admin, $"Race question {i}?", "Yes", "No"));
        var examId = await CreateExamAsync(admin, "Race", ids, TimeSpan.FromMinutes(-5));

        // Several candidates one after another, because one run may happen not to overlap and the point is that none ever disagrees.
        for (var round = 0; round < 6; round++)
        {
            var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
            using var _c = candidate;
            var started = await JsonAsync((await candidate.PostAsJsonAsync($"/v1/me/exams/{examId}/attempts", new { instructionsAcknowledged = true })).EnsureSuccessStatusCode());
            var attemptId = started.GetProperty("id").GetGuid();
            var questions = started.GetProperty("sections")[0].GetProperty("questions").EnumerateArray().ToList();

            var gate = new TaskCompletionSource();
            var saves = questions.Select(async q =>
            {
                var yes = q.GetProperty("options").EnumerateArray().Single(o => o.GetProperty("text").GetString() == "Yes").GetProperty("id").GetGuid();
                await gate.Task;
                return await candidate.PutAsJsonAsync($"/v1/me/attempts/{attemptId}/answers/{q.GetProperty("id").GetGuid()}", new { optionId = yes });
            }).ToList();
            var submit = Task.Run(async () =>
            {
                await gate.Task;
                return await candidate.PostAsync($"/v1/me/attempts/{attemptId}/submit", content: null);
            });

            gate.SetResult();
            var responses = await Task.WhenAll(saves);
            var submitted = await submit;

            // A save is accepted, or refused because the attempt was already over (or a clash the page repeats); nothing else is possible.
            Assert.All(responses, r => Assert.True(r.IsSuccessStatusCode || r.StatusCode == HttpStatusCode.Conflict, $"A save answered {(int)r.StatusCode}."));
            submitted.EnsureSuccessStatusCode();

            var paper = await JsonAsync((await admin.GetAsync($"/v1/exams/{examId}/attempts/{attemptId}/paper")).EnsureSuccessStatusCode());
            var shown = paper.GetProperty("sections")[0].GetProperty("questions").EnumerateArray().ToList();
            var stored = shown.Count(q => q.GetProperty("options").EnumerateArray().Any(o => o.GetProperty("wasChosen").GetBoolean()));
            var accepted = responses.Count(r => r.IsSuccessStatusCode);

            // Every accepted save is a stored answer, and the stored score is exactly what those answers earn.
            Assert.Equal(accepted, stored);
            Assert.Equal(shown.Sum(q => q.GetProperty("marks").GetDecimal()), paper.GetProperty("score").GetDecimal());
        }
    }
}
