using System.Net.Http.Json;
using System.Text.Json;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// Every setting an author can change on an exam (FR-12, FR-13, FR-22, FR-23, FR-34, FR-46) leaves the exam's marking scheme as it was.
/// The scheme is stored inside the exam's config, and a setting that hands the old scheme to the new config makes EF Core write NULL over its
/// columns. No unit test can see that write, because it only happens when the exam is saved to a database, so this one runs each setting
/// over HTTP against a real Postgres and reads the exam back with a fresh request after each change.
/// </summary>
public sealed class ExamConfigChangeFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record Marks(decimal Correct, decimal Incorrect, decimal Unattempted, bool PartialCredit);

    /// <summary>One call an author makes. <paramref name="MarksAfter"/> is the scheme the call sets, when it is the call that changes the scheme.</summary>
    private sealed record Step(HttpMethod Method, string Route, object? Body = null, Marks? MarksAfter = null);

    // Nothing here is the default (1, 0, 0, no partial credit), so a scheme that was lost and rebuilt from defaults would show.
    private static readonly Marks Authored = new(4m, -1m, -0.25m, PartialCredit: true);

    private static Step Put(string route, object body) => new(HttpMethod.Put, route, body);

    /// <summary>Each setting, as the calls an author makes to change it. One entry per way <c>Exam</c> changes its config.</summary>
    private static Dictionary<string, Step[]> Settings()
    {
        var start = DateTime.UtcNow.AddHours(1);
        return new()
        {
            ["marking-scheme"] = [new(HttpMethod.Put, "/marking-scheme", new { correctMarks = 2, incorrectMarks = 0, unattemptedMarks = -0.5, partialCredit = false }, new Marks(2m, 0m, -0.5m, false))],
            ["schedule"] = [Put("/schedule", new { scheduledStartTime = start, scheduledEndTime = start.AddHours(3), timeZone = "Asia/Kolkata", durationMinutes = 45 })],
            ["shuffle"] = [Put("/shuffle", new { shuffleQuestions = true, shuffleOptions = true })],
            ["attempt-limit"] = [Put("/attempt-limit", new { maxAttempts = 3 })],
            ["content-protection"] = [Put("/content-protection", new { contentProtection = false })],
            ["proctoring-profile"] = [Put("/proctoring-profile", new { profile = "BROWSER_LOCK" })],
            ["focus-violation-limit"] = [Put("/focus-violation-limit", new { focusViolationLimit = 5 })],
            ["focus-warn-at"] = [Put("/focus-violation-limit", new { focusViolationLimit = 5 }), Put("/focus-warn-at", new { focusWarnAt = 2 })],
            ["passing-percentage"] = [Put("/passing-percentage", new { passingPercentage = 60 })],
            ["passing-percentage-cleared"] = [Put("/passing-percentage", new { passingPercentage = 60 }), new(HttpMethod.Delete, "/passing-percentage")],
            ["result-release-scheduled"] = [Put("/result-release", new { mode = "Scheduled", releaseTime = DateTime.UtcNow.AddDays(2) })],
            ["result-release-manual"] = [Put("/result-release", new { mode = "Manual" })],
            ["results-released-by-hand"] =
            [
                Put("/result-release", new { mode = "Manual" }),
                new(HttpMethod.Post, "/publish"),
                new(HttpMethod.Post, "/results/release"),
            ],
        };
    }

    /// <summary>The names of the settings, so each one is its own test.</summary>
    public static TheoryData<string> SettingNames() => [.. Settings().Keys];

    private static async Task<Marks> MarksOfAsync(HttpClient admin, Guid examId)
    {
        var scheme = (await admin.GetFromJsonAsync<JsonElement>($"/v1/exams/{examId}")).GetProperty("config").GetProperty("markingScheme");
        return new Marks(
            scheme.GetProperty("correctMarks").GetDecimal(),
            scheme.GetProperty("incorrectMarks").GetDecimal(),
            scheme.GetProperty("unattemptedMarks").GetDecimal(),
            scheme.GetProperty("partialCredit").GetBoolean());
    }

    [Theory]
    [MemberData(nameof(SettingNames))]
    public async Task ChangingASetting_LeavesTheMarkingSchemeAsItWas(string setting)
    {
        using var admin = await factory.AdminClientAsync();
        var question = await CreateQuestionAsync(admin, $"Config question ({setting})", "Right", "Wrong");
        var examId = await CreateExamAsync(admin, $"Config exam ({setting})", [question], TimeSpan.FromMinutes(-5), publish: false);
        (await admin.PutAsJsonAsync(
            $"/v1/exams/{examId}/marking-scheme",
            new { correctMarks = Authored.Correct, incorrectMarks = Authored.Incorrect, unattemptedMarks = Authored.Unattempted, partialCredit = Authored.PartialCredit }))
            .EnsureSuccessStatusCode();
        var expected = Authored;
        Assert.Equal(expected, await MarksOfAsync(admin, examId));

        foreach (var step in Settings()[setting])
        {
            using var request = new HttpRequestMessage(step.Method, $"/v1/exams/{examId}{step.Route}")
            {
                Content = step.Body is null ? null : JsonContent.Create(step.Body),
            };
            await ServerErrorLog.EnsureSuccessAsync(await admin.SendAsync(request));

            // A fresh request, so the exam is read back from the database and not from anything the last request held in memory.
            expected = step.MarksAfter ?? expected;
            Assert.Equal(expected, await MarksOfAsync(admin, examId));
        }
    }
}
