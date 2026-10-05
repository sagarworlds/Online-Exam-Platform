using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>A question's version history over real HTTP and a real database (FR-7).</summary>
public sealed class QuestionHistoryFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static Task<HttpResponseMessage> EditAsync(HttpClient admin, Guid id, string text, params object[] options) =>
        admin.PutAsJsonAsync($"/v1/questions/{id}", new { text, options });

    private static object Keep(JsonElement option, string? text = null, bool? isCorrect = null) =>
        new { id = option.GetProperty("id").GetGuid(), text = text ?? option.GetProperty("text").GetString(), isCorrect = isCorrect ?? option.GetProperty("isCorrect").GetBoolean() };

    [Fact]
    public async Task ANewQuestion_HasOneVersion_MatchingWhatWasCreated()
    {
        using var admin = await factory.AdminClientAsync();
        var id = await CreateQuestionAsync(admin, "Capital of France?", "Paris", "Rome");

        var history = await admin.GetFromJsonAsync<JsonElement>($"/v1/questions/{id}/history");

        var versions = history.EnumerateArray().ToList();
        var version = Assert.Single(versions);
        Assert.Equal(1, version.GetProperty("versionNumber").GetInt32());
        Assert.Equal("Capital of France?", version.GetProperty("text").GetString());
        Assert.Equal(["Paris", "Rome"], version.GetProperty("options").EnumerateArray().Select(o => o.GetProperty("text").GetString()));
    }

    [Fact]
    public async Task EditingSuccessfully_AddsAVersion_AndTheEarlierOneStillReadsAsItDid()
    {
        using var admin = await factory.AdminClientAsync();
        var id = await CreateQuestionAsync(admin, "Capital of France?", "Paris", "Rome");
        var options = (await admin.GetFromJsonAsync<JsonElement>($"/v1/questions/{id}")).GetProperty("options").EnumerateArray().ToList();

        await EditAsync(admin, id, "Capital of Spain?", Keep(options[0], text: "Madrid"), Keep(options[1], text: "Lisbon"));

        var history = await admin.GetFromJsonAsync<JsonElement>($"/v1/questions/{id}/history");
        var versions = history.EnumerateArray().ToList();
        Assert.Equal(2, versions.Count);
        Assert.Equal("Capital of France?", versions[0].GetProperty("text").GetString());
        Assert.Equal(["Paris", "Rome"], versions[0].GetProperty("options").EnumerateArray().Select(o => o.GetProperty("text").GetString()));
        Assert.Equal("Capital of Spain?", versions[1].GetProperty("text").GetString());
        Assert.Equal(["Madrid", "Lisbon"], versions[1].GetProperty("options").EnumerateArray().Select(o => o.GetProperty("text").GetString()));
    }

    [Fact]
    public async Task ARejectedEdit_AddsNoVersion()
    {
        using var admin = await factory.AdminClientAsync();
        var id = await CreateQuestionAsync(admin, "Q?", "A", "B");
        var options = (await admin.GetFromJsonAsync<JsonElement>($"/v1/questions/{id}")).GetProperty("options").EnumerateArray().ToList();

        await EditAsync(admin, id, "Q?", Keep(options[0], text: ""), Keep(options[1]));

        var history = await admin.GetFromJsonAsync<JsonElement>($"/v1/questions/{id}/history");
        Assert.Equal(1, history.EnumerateArray().Count());
    }

    [Fact]
    public async Task CorrectingTheAnswerKey_AddsAVersion()
    {
        using var admin = await factory.AdminClientAsync();
        var id = await CreateQuestionAsync(admin, "Q?", "A", "B");
        var options = (await admin.GetFromJsonAsync<JsonElement>($"/v1/questions/{id}")).GetProperty("options").EnumerateArray().ToList();
        var b = options[1].GetProperty("id").GetGuid();

        (await admin.PostAsJsonAsync($"/v1/questions/{id}/correct-answer-key", new { correctOptionIds = new[] { b }, reason = "Key was wrong" }))
            .EnsureSuccessStatusCode();

        var history = await admin.GetFromJsonAsync<JsonElement>($"/v1/questions/{id}/history");
        var versions = history.EnumerateArray().ToList();
        Assert.Equal(2, versions.Count);
        Assert.Equal(b, versions[1].GetProperty("options").EnumerateArray().Single(o => o.GetProperty("isCorrect").GetBoolean()).GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task AnUnknownQuestion_Is404()
    {
        using var admin = await factory.AdminClientAsync();

        var response = await admin.GetAsync($"/v1/questions/{Guid.NewGuid()}/history");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await JsonAsync(response);
        Assert.Equal("question_not_found", problem.GetProperty("title").GetString());
    }
}
