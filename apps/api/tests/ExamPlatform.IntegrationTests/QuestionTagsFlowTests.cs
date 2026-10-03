using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ExamPlatform.IntegrationTests;

/// <summary>Difficulty and topic labels on questions over real HTTP and a real database: saved with the question, filterable, and listed.</summary>
public sealed class QuestionTagsFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<Guid> CreateAsync(HttpClient admin, string text, string? difficulty, params string[] topics)
    {
        var response = await admin.PostAsJsonAsync("/v1/questions", new
        {
            text,
            options = new[] { new { text = "A", isCorrect = true }, new { text = "B", isCorrect = false } },
            difficulty,
            topics,
        });
        return (await JsonAsync(response.EnsureSuccessStatusCode())).GetProperty("id").GetGuid();
    }

    private static async Task<List<Guid>> ListIdsAsync(HttpClient admin, string query) =>
        (await admin.GetFromJsonAsync<JsonElement>($"/v1/questions?{query}")).EnumerateArray().Select(q => q.GetProperty("id").GetGuid()).ToList();

    [Fact]
    public async Task ACreatedQuestion_ComesBackWithItsLabelsCleaned()
    {
        using var admin = await factory.AdminClientAsync();
        var topic = $"Fractions-{Guid.NewGuid():N}";

        var id = await CreateAsync(admin, "Half of ten?", "Hard", $"  {topic} ", topic.ToUpperInvariant());

        var question = await admin.GetFromJsonAsync<JsonElement>($"/v1/questions/{id}");
        Assert.Equal("hard", question.GetProperty("difficulty").GetString());
        Assert.Equal([topic.ToLowerInvariant()], question.GetProperty("topics").EnumerateArray().Select(t => t.GetString()));
    }

    [Fact]
    public async Task AQuestionWithoutLabels_HasNoDifficultyAndNoTopics()
    {
        using var admin = await factory.AdminClientAsync();
        var id = await CreateAsync(admin, "Plain", null);

        var question = await admin.GetFromJsonAsync<JsonElement>($"/v1/questions/{id}");

        Assert.Equal(JsonValueKind.Null, question.GetProperty("difficulty").ValueKind);
        Assert.Empty(question.GetProperty("topics").EnumerateArray());
    }

    [Fact]
    public async Task TheListCanBeNarrowedByDifficultyAndByTopic()
    {
        using var admin = await factory.AdminClientAsync();
        var topic = $"t{Guid.NewGuid():N}";
        var easy = await CreateAsync(admin, "Easy one", "easy", topic);
        var hard = await CreateAsync(admin, "Hard one", "hard", topic, "other");
        var elsewhere = await CreateAsync(admin, "Elsewhere", "hard", "other");

        var onTopic = await ListIdsAsync(admin, $"topic={topic}");
        Assert.Contains(easy, onTopic);
        Assert.Contains(hard, onTopic);
        Assert.DoesNotContain(elsewhere, onTopic);

        // The topic filter is case-insensitive because topics are stored lower case.
        Assert.Equal(onTopic, await ListIdsAsync(admin, $"topic={topic.ToUpperInvariant()}"));

        var hardOnTopic = await ListIdsAsync(admin, $"topic={topic}&difficulty=hard");
        Assert.Equal([hard], hardOnTopic);
    }

    [Fact]
    public async Task TheTopicsInUse_AreListedOnceEach()
    {
        using var admin = await factory.AdminClientAsync();
        var topic = $"listed-{Guid.NewGuid():N}";
        await CreateAsync(admin, "One", null, topic);
        await CreateAsync(admin, "Two", null, topic);

        var topics = (await admin.GetFromJsonAsync<JsonElement>("/v1/questions/topics")).EnumerateArray().Select(t => t.GetString()).ToList();

        Assert.Single(topics, topic);
        Assert.Equal(topics.OrderBy(t => t, StringComparer.Ordinal), topics);
    }

    [Fact]
    public async Task AnEdit_ReplacesTheLabels()
    {
        using var admin = await factory.AdminClientAsync();
        var id = await CreateAsync(admin, "Q?", "easy", "old");
        var options = (await admin.GetFromJsonAsync<JsonElement>($"/v1/questions/{id}")).GetProperty("options").EnumerateArray()
            .Select(o => new { id = o.GetProperty("id").GetGuid(), text = o.GetProperty("text").GetString(), isCorrect = o.GetProperty("isCorrect").GetBoolean() })
            .ToArray();

        var edited = await JsonAsync((await admin.PutAsJsonAsync($"/v1/questions/{id}", new { text = "Q?", options, difficulty = "medium", topics = new[] { "new" } })).EnsureSuccessStatusCode());

        Assert.Equal("medium", edited.GetProperty("difficulty").GetString());
        Assert.Equal(["new"], edited.GetProperty("topics").EnumerateArray().Select(t => t.GetString()));
    }

    [Fact]
    public async Task ABadDifficulty_Is400()
    {
        using var admin = await factory.AdminClientAsync();

        var response = await admin.PostAsJsonAsync("/v1/questions", new
        {
            text = "Q?",
            options = new[] { new { text = "A", isCorrect = true }, new { text = "B", isCorrect = false } },
            difficulty = "tricky",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_question", (await JsonAsync(response)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task ABadDifficultyFilter_Is400()
    {
        using var admin = await factory.AdminClientAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/v1/questions?difficulty=tricky")).StatusCode);
    }
}
