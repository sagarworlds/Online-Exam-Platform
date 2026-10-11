using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ExamPlatform.IntegrationTests;

/// <summary>Searching the question bank by text over real HTTP and a real database: question text and option text, case-insensitively.</summary>
public sealed class QuestionSearchFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static string Unique() => Guid.NewGuid().ToString("N")[..10];

    private static async Task<Guid> CreateAsync(HttpClient admin, string html, string correct = "Yes", string wrong = "No", string? difficulty = null)
    {
        var response = await admin.PostAsJsonAsync("/v1/questions", new
        {
            text = html,
            options = new[] { new { text = correct, isCorrect = true }, new { text = wrong, isCorrect = false } },
            difficulty,
        });
        await ServerErrorLog.EnsureSuccessAsync(response);
        return (await JsonAsync(response)).GetProperty("id").GetGuid();
    }

    private static async Task<List<Guid>> SearchAsync(HttpClient admin, string query, string extra = "") =>
        (await admin.GetFromJsonAsync<JsonElement>($"/v1/questions?q={Uri.EscapeDataString(query)}{extra}")).EnumerateArray()
            .Select(q => q.GetProperty("id").GetGuid()).ToList();

    [Fact]
    public async Task FindsAQuestionByItsText_IgnoringCase_AndAnythingElseIsLeftOut()
    {
        using var admin = await factory.AdminClientAsync();
        var word = $"zebra{Unique()}";
        var match = await CreateAsync(admin, $"<p>The <strong>{word}</strong> crossing</p>");
        var other = await CreateAsync(admin, "<p>Something unrelated</p>");

        var found = await SearchAsync(admin, word.ToUpperInvariant());

        Assert.Equal([match], found);
        Assert.DoesNotContain(other, found);
    }

    [Fact]
    public async Task FindsAQuestionByTheTextOfOneOfItsOptions()
    {
        using var admin = await factory.AdminClientAsync();
        var word = $"option{Unique()}";
        var match = await CreateAsync(admin, "<p>Pick one</p>", correct: $"The {word} answer");

        Assert.Equal([match], await SearchAsync(admin, word));
    }

    [Fact]
    public async Task DoesNotMatchMarkup_OnlyWhatACandidateReads()
    {
        using var admin = await factory.AdminClientAsync();
        var inner = $"bold{Unique()}";
        await CreateAsync(admin, $"<p>text with <strong>{inner}</strong> inside</p>");

        // "<strong>" is markup, not something anyone wrote: no question may be found by it.
        Assert.Empty(await SearchAsync(admin, "<strong>"));
        Assert.Single(await SearchAsync(admin, inner));
    }

    [Fact]
    public async Task APercentOrUnderscore_MatchesItself_NotEverything()
    {
        using var admin = await factory.AdminClientAsync();
        var tag = Unique();
        var percent = await CreateAsync(admin, $"<p>Score {tag} was 100% sure</p>");
        await CreateAsync(admin, $"<p>Score {tag} was 100 sure</p>");
        var underscore = await CreateAsync(admin, $"<p>Variable snake_case{tag}</p>");
        await CreateAsync(admin, $"<p>Variable snakeXcase{tag}</p>");

        Assert.Equal([percent], await SearchAsync(admin, $"{tag} was 100%"));
        Assert.Equal([underscore], await SearchAsync(admin, $"snake_case{tag}"));
    }

    [Fact]
    public async Task CombinesWithTheOtherFilters()
    {
        using var admin = await factory.AdminClientAsync();
        var word = $"mix{Unique()}";
        var hard = await CreateAsync(admin, $"<p>{word} hard one</p>", difficulty: "hard");
        await CreateAsync(admin, $"<p>{word} easy one</p>", difficulty: "easy");

        Assert.Equal([hard], await SearchAsync(admin, word, "&difficulty=hard"));
    }

    [Fact]
    public async Task AnEdit_UpdatesWhatSearchFinds()
    {
        using var admin = await factory.AdminClientAsync();
        var before = $"before{Unique()}";
        var after = $"after{Unique()}";
        var id = await CreateAsync(admin, $"<p>{before}</p>");
        var options = (await admin.GetFromJsonAsync<JsonElement>($"/v1/questions/{id}")).GetProperty("options").EnumerateArray()
            .Select(o => new { id = o.GetProperty("id").GetGuid(), text = o.GetProperty("text").GetString(), isCorrect = o.GetProperty("isCorrect").GetBoolean() })
            .ToArray();

        (await admin.PutAsJsonAsync($"/v1/questions/{id}", new { text = $"<p>{after}</p>", options })).EnsureSuccessStatusCode();

        Assert.Empty(await SearchAsync(admin, before));
        Assert.Equal([id], await SearchAsync(admin, after));
    }

    [Fact]
    public async Task ABlankSearch_ListsEverythingAsBefore()
    {
        using var admin = await factory.AdminClientAsync();
        var id = await CreateAsync(admin, $"<p>blank{Unique()}</p>");

        Assert.Contains(id, await SearchAsync(admin, "   "));
    }
}
