using System.Net.Http.Json;
using System.Text.Json;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// How many SQL commands a candidate's two commonest calls take once the caches are warm. Every command is a round trip to a database that is
/// not on the same machine in production, so the count is a better guard than a time, which depends on the machine: it only moves when the
/// code changes. A failing run prints the commands, so the one that crept in can be seen.
/// </summary>
/// <remarks>
/// The budgets are what the calls take today, with no slack, so that losing any one of the caches, or the slim answer load, fails a test: each
/// of those costs at least one more command. The questions here are not filed under a chapter, which is the cheapest case; filing them adds
/// the read of where they are filed. A call is measured three times and the fewest counts, because a command that belongs to something else
/// (a background timer in the host) can land inside one measurement but not all three.
/// </remarks>
public sealed class AnswerPathStatementBudgetTests(SqlCountingApiFactory factory) : IClassFixture<SqlCountingApiFactory>
{
    // Saving or clearing an answer: the session check, the lock, the attempt with the one answer, its paper, where the question is filed, and the write.
    private const int SaveBudget = 6;

    // Reading the attempt: the session check, the attempt with its ten collections, and where the questions are filed.
    private const int ReadBudget = 12;

    private const int Measurements = 3;

    private sealed record Sitting(HttpClient Admin, HttpClient Candidate, Guid AttemptId, IReadOnlyList<(Guid Question, Guid Option)> Questions) : IDisposable
    {
        public void Dispose()
        {
            Admin.Dispose();
            Candidate.Dispose();
        }

        public string AnswerUrl(int question) => $"/v1/me/attempts/{AttemptId}/answers/{Questions[question].Question}";
    }

    private async Task<Sitting> StartedAsync()
    {
        var admin = await factory.AdminClientAsync();
        var ids = new List<Guid>();
        for (var i = 1; i <= 5; i++)
        {
            ids.Add(await CreateQuestionAsync(admin, $"Budget question {i}", "Right", "Wrong"));
        }

        var examId = await CreateExamAsync(admin, "Statement budget exam", ids, TimeSpan.FromMinutes(-5), durationMinutes: 60);
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        var start = await candidate.PostAsJsonAsync($"/v1/me/exams/{examId}/attempts", new { instructionsAcknowledged = true });
        start.EnsureSuccessStatusCode();
        var body = await start.Content.ReadFromJsonAsync<JsonElement>();
        var questions = body.GetProperty("sections").EnumerateArray()
            .SelectMany(s => s.GetProperty("questions").EnumerateArray())
            .Select(q => (q.GetProperty("id").GetGuid(), q.GetProperty("options")[0].GetProperty("id").GetGuid()))
            .ToList();
        var sitting = new Sitting(admin, candidate, body.GetProperty("id").GetGuid(), questions);

        // The first call a candidate makes fills the caches for the exam and its questions.
        (await candidate.GetAsync($"/v1/me/attempts/{sitting.AttemptId}")).EnsureSuccessStatusCode();
        return sitting;
    }

    /// <summary>Runs a call <see cref="Measurements"/> times, each by <paramref name="call"/> given its turn, and fails unless the fewest commands counted are within the budget.</summary>
    private async Task AssertWithinBudgetAsync(string what, int budget, Func<int, Task<HttpResponseMessage>> call)
    {
        var fewest = int.MaxValue;
        IReadOnlyList<string> statements = [];
        for (var turn = 0; turn < Measurements; turn++)
        {
            factory.Sql.Reset(keepText: true);
            using var response = await call(turn);
            response.EnsureSuccessStatusCode();
            if (factory.Sql.Count < fewest)
            {
                fewest = factory.Sql.Count;
                statements = factory.Sql.Statements();
            }
        }

        Assert.True(
            fewest <= budget,
            $"{what} took {fewest} SQL commands at the fewest of {Measurements} tries, budget {budget}:\n  "
            + string.Join("\n  ", statements.Select(s => s[..Math.Min(s.Length, 200)])));
    }

    [Fact]
    public async Task TheFirstAnswerToAQuestion_OnceTheCachesAreWarm_StaysWithinItsBudget()
    {
        using var sitting = await StartedAsync();

        // A different question each time, so every try is a first answer.
        await AssertWithinBudgetAsync("The first answer to a question", SaveBudget, turn =>
            sitting.Candidate.PutAsJsonAsync(sitting.AnswerUrl(turn), new { optionId = sitting.Questions[turn].Option, sequence = 1 }));
    }

    [Fact]
    public async Task ReplacingAnAnswer_OnceTheCachesAreWarm_StaysWithinTheSameBudget()
    {
        using var sitting = await StartedAsync();
        (await sitting.Candidate.PutAsJsonAsync(sitting.AnswerUrl(0), new { optionId = sitting.Questions[0].Option, sequence = 1 })).EnsureSuccessStatusCode();

        await AssertWithinBudgetAsync("Replacing an answer", SaveBudget, turn =>
            sitting.Candidate.PutAsJsonAsync(sitting.AnswerUrl(0), new { optionId = sitting.Questions[0].Option, sequence = turn + 2 }));
    }

    [Fact]
    public async Task AClear_OnceTheCachesAreWarm_StaysWithinTheSameBudget()
    {
        using var sitting = await StartedAsync();
        for (var question = 0; question < Measurements; question++)
        {
            (await sitting.Candidate.PutAsJsonAsync(sitting.AnswerUrl(question), new { optionId = sitting.Questions[question].Option, sequence = 1 })).EnsureSuccessStatusCode();
        }

        await AssertWithinBudgetAsync("Clearing an answer", SaveBudget, turn =>
            sitting.Candidate.DeleteAsync($"{sitting.AnswerUrl(turn)}?sequence=2"));
    }

    [Fact]
    public async Task AnAttemptRead_OnceTheCachesAreWarm_StaysWithinItsBudget()
    {
        using var sitting = await StartedAsync();

        await AssertWithinBudgetAsync("Reading the attempt", ReadBudget, _ =>
            sitting.Candidate.GetAsync($"/v1/me/attempts/{sitting.AttemptId}"));
    }
}
