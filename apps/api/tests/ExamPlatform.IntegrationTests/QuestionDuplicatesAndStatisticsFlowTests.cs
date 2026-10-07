using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>FR-9: a question that repeats one in the bank is refused unless the author says otherwise, and a question reports how candidates did on it.</summary>
public sealed class QuestionDuplicatesAndStatisticsFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static object Body(string text, string first, string second, bool allowDuplicate = false) => new
    {
        text,
        options = new[] { new { text = first, isCorrect = true }, new { text = second, isCorrect = false } },
        allowDuplicate,
    };

    /// <summary>A host that refuses repeats, which the shared test host turns off so its suites can create the same question freely.</summary>
    private async Task<(IAsyncDisposable Host, HttpClient Admin)> StrictAdminAsync()
    {
        var strict = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["QuestionBank:RefuseDuplicates"] = "true" })));
        var admin = strict.CreateClient();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await factory.SignInAsAsync("SuperAdmin")).AccessToken);
        return (strict, admin);
    }

    [Fact]
    public async Task ARepeatedQuestion_IsRefused_UntilTheAuthorSaysToAddItAnyway()
    {
        var (host, admin) = await StrictAdminAsync();
        await using var _h = host;
        using var _a = admin;

        (await admin.PostAsJsonAsync("/v1/questions", Body("Capital of Duplicateland?", "Dupville", "Other"))).EnsureSuccessStatusCode();

        var again = await admin.PostAsJsonAsync("/v1/questions", Body("<p>capital of  DUPLICATELAND</p>", "Other", "dupville"));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("duplicate_question", (await JsonAsync(again)).GetProperty("title").GetString());

        // The same wording with other options is a different question, and a repeat can be added on purpose.
        (await admin.PostAsJsonAsync("/v1/questions", Body("Capital of Duplicateland?", "Dupville", "Third"))).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync("/v1/questions", Body("Capital of Duplicateland?", "Dupville", "Other", allowDuplicate: true))).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task TheCheck_ListsWhatRepeatsAQuestion_SameOptionsFirst()
    {
        using var admin = await factory.AdminClientAsync();
        var same = await CreateQuestionAsync(admin, "Capital of Checkland?", "Checkton", "Other");
        var other = await CreateQuestionAsync(admin, "Capital of Checkland?", "Checkton", "Third");

        var response = await admin.PostAsJsonAsync("/v1/questions/duplicates", new { text = "<p>Capital of Checkland?</p>", options = new[] { "Other", "Checkton" } });

        response.EnsureSuccessStatusCode();
        var found = (await JsonAsync(response)).EnumerateArray().ToList();
        Assert.Equal([same, other], found.Select(f => f.GetProperty("id").GetGuid()));
        Assert.Equal([true, false], found.Select(f => f.GetProperty("sameOptions").GetBoolean()));
        Assert.Equal("Capital of Checkland?", found[0].GetProperty("preview").GetString());

        // A question being edited is not a duplicate of itself.
        var excluded = await JsonAsync(await admin.PostAsJsonAsync("/v1/questions/duplicates",
            new { text = "Capital of Checkland?", options = new[] { "Other", "Checkton" }, excludeQuestionId = same }));
        Assert.Equal([other], excluded.EnumerateArray().Select(f => f.GetProperty("id").GetGuid()));
    }

    [Fact]
    public async Task AnImport_LeavesOutRepeats_AndReportsThem()
    {
        var (host, admin) = await StrictAdminAsync();
        await using var _h = host;
        using var _a = admin;
        (await admin.PostAsJsonAsync("/v1/questions", Body("Capital of Importland?", "Importville", "Other"))).EnsureSuccessStatusCode();
        const string header = "Text,Option1,Correct1,Option2,Correct2,Option3,Correct3,Option4,Correct4,Option5,Correct5,Option6,Correct6,AllowsMultiple,Difficulty,Topics\r\n";
        var csv = header
            + "Capital of Importland?,Importville,true,Other,false,,,,,,,,,false,,\r\n"
            + "Capital of Newland?,Newton,true,Other,false,,,,,,,,,false,,\r\n";

        var result = await JsonAsync((await admin.PostAsJsonAsync("/v1/questions/import", new { format = "csv", content = csv })).EnsureSuccessStatusCode());

        Assert.Equal([3], result.GetProperty("created").EnumerateArray().Select(c => c.GetProperty("row").GetInt32()));
        var skipped = Assert.Single(result.GetProperty("duplicates").EnumerateArray());
        Assert.Equal(2, skipped.GetProperty("row").GetInt32());
        Assert.Contains("already has this question", skipped.GetProperty("reason").GetString());

        var again = await JsonAsync((await admin.PostAsJsonAsync("/v1/questions/import", new { format = "csv", content = csv, allowDuplicates = true })).EnsureSuccessStatusCode());
        Assert.Equal(2, again.GetProperty("created").GetArrayLength());
    }

    [Fact]
    public async Task AQuestionReportsHowCandidatesDidOnIt()
    {
        using var admin = await factory.AdminClientAsync();
        var questionId = await CreateQuestionAsync(admin, "Statistics question?", "Yes", "No");
        var examId = await CreateExamAsync(admin, "Statistics exam", [questionId], TimeSpan.FromMinutes(-5));

        var before = await JsonAsync((await admin.GetAsync($"/v1/questions/{questionId}/statistics")).EnsureSuccessStatusCode());
        Assert.Equal(1, before.GetProperty("examCount").GetInt32());
        Assert.Equal(0, before.GetProperty("answered").GetInt32());
        Assert.Equal(JsonValueKind.Null, before.GetProperty("percentCorrect").ValueKind);

        foreach (var choose in new[] { "Yes", "Yes", "No" })
        {
            var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
            using var _c = candidate;
            var started = await JsonAsync((await candidate.PostAsJsonAsync($"/v1/me/exams/{examId}/attempts", new { instructionsAcknowledged = true })).EnsureSuccessStatusCode());
            var attemptId = started.GetProperty("id").GetGuid();
            var question = started.GetProperty("sections")[0].GetProperty("questions")[0];
            var option = question.GetProperty("options").EnumerateArray().Single(o => o.GetProperty("text").GetString() == choose).GetProperty("id").GetGuid();
            (await candidate.PutAsJsonAsync($"/v1/me/attempts/{attemptId}/answers/{question.GetProperty("id").GetGuid()}", new { optionId = option })).EnsureSuccessStatusCode();
            (await candidate.PostAsync($"/v1/me/attempts/{attemptId}/submit", content: null)).EnsureSuccessStatusCode();
        }

        var stats = await JsonAsync((await admin.GetAsync($"/v1/questions/{questionId}/statistics")).EnsureSuccessStatusCode());

        Assert.Equal((3, 2, 66.7m), (stats.GetProperty("answered").GetInt32(), stats.GetProperty("correct").GetInt32(), stats.GetProperty("percentCorrect").GetDecimal()));
        Assert.Equal(["Statistics exam"], stats.GetProperty("examNames").EnumerateArray().Select(n => n.GetString()));
        var options = stats.GetProperty("options").EnumerateArray().ToDictionary(o => o.GetProperty("text").GetString()!, o => o.GetProperty("timesChosen").GetInt32());
        Assert.Equal((2, 1), (options["Yes"], options["No"]));
    }
}
