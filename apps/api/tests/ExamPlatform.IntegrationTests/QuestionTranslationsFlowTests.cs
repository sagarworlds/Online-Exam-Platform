using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>FR-10: questions in English and Hindi, linked so each can be found from the other, with the answer key kept in step.</summary>
public sealed class QuestionTranslationsFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static object Translation(string language, string text, params string[] options) => new { language, text, options };

    [Fact]
    public async Task AQuestionIsEnglishUntilSaidOtherwise_AndCanBeFilteredByLanguage()
    {
        using var admin = await factory.AdminClientAsync();
        var english = await CreateQuestionAsync(admin, "Language default question?", "Yes", "No");
        var hindi = await JsonAsync((await admin.PostAsJsonAsync("/v1/questions", new
        {
            text = "भाषा परीक्षा प्रश्न?",
            options = new[] { new { text = "हाँ", isCorrect = true }, new { text = "नहीं", isCorrect = false } },
            language = "hi",
        })).EnsureSuccessStatusCode());

        var shown = await JsonAsync((await admin.GetAsync($"/v1/questions/{english}")).EnsureSuccessStatusCode());
        Assert.Equal("en", shown.GetProperty("language").GetString());
        Assert.Equal(english, shown.GetProperty("translationGroupId").GetGuid());
        Assert.Equal("hi", hindi.GetProperty("language").GetString());

        var inHindi = await JsonAsync((await admin.GetAsync("/v1/questions?language=hi")).EnsureSuccessStatusCode());
        var ids = inHindi.EnumerateArray().Select(q => q.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(hindi.GetProperty("id").GetGuid(), ids);
        Assert.DoesNotContain(english, ids);
        Assert.All(inHindi.EnumerateArray(), q => Assert.Equal("hi", q.GetProperty("language").GetString()));
    }

    [Fact]
    public async Task AQuestionInAnUnsupportedLanguage_IsRefused()
    {
        using var admin = await factory.AdminClientAsync();

        var response = await admin.PostAsJsonAsync("/v1/questions", new
        {
            text = "Unsupported language?",
            options = new[] { new { text = "Oui", isCorrect = true }, new { text = "Non", isCorrect = false } },
            language = "fr",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/v1/questions?language=fr")).StatusCode);
    }

    [Fact]
    public async Task ATranslation_IsLinkedToItsSource_AndTakesItsAnswerKey()
    {
        using var admin = await factory.AdminClientAsync();
        var source = await CreateQuestionAsync(admin, "Capital of Translationland?", "Right", "Wrong");

        var response = await admin.PostAsJsonAsync($"/v1/questions/{source}/translations", Translation("hi", "<p>अनुवाद देश की राजधानी?</p>", "सही", "गलत"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var translated = await JsonAsync(response);
        Assert.Equal("hi", translated.GetProperty("language").GetString());
        Assert.Equal(source, translated.GetProperty("translationGroupId").GetGuid());
        Assert.Equal("draft", translated.GetProperty("status").GetString());
        // The translator sent words only; which option is right came from the source, in the same order.
        var options = translated.GetProperty("options").EnumerateArray().ToList();
        Assert.Equal(["सही", "गलत"], options.Select(o => o.GetProperty("text").GetString()));
        Assert.Equal([true, false], options.Select(o => o.GetProperty("isCorrect").GetBoolean()));

        var listed = await JsonAsync((await admin.GetAsync($"/v1/questions/{translated.GetProperty("id").GetGuid()}/translations")).EnsureSuccessStatusCode());
        Assert.Equal(["en", "hi"], listed.EnumerateArray().Select(t => t.GetProperty("language").GetString()));
        Assert.Equal([source, translated.GetProperty("id").GetGuid()], listed.EnumerateArray().Select(t => t.GetProperty("id").GetGuid()));
    }

    [Fact]
    public async Task AGroupCanHoldEnglishHindiAndMarathi_ListedInThatOrder()
    {
        using var admin = await factory.AdminClientAsync();
        var source = await CreateQuestionAsync(admin, "Three language question?", "Right", "Wrong");

        // Added Marathi first, to show the order of the list is the order languages are offered, not the order they were added.
        var marathi = await JsonAsync((await admin.PostAsJsonAsync($"/v1/questions/{source}/translations", Translation("MR", "<p>तीन भाषा प्रश्न?</p>", "बरोबर", "चूक"))).EnsureSuccessStatusCode());
        var hindi = await JsonAsync((await admin.PostAsJsonAsync($"/v1/questions/{source}/translations", Translation("hi", "<p>तीन भाषा प्रश्न</p>", "सही", "गलत"))).EnsureSuccessStatusCode());

        Assert.Equal("mr", marathi.GetProperty("language").GetString());
        var listed = await JsonAsync((await admin.GetAsync($"/v1/questions/{source}/translations")).EnsureSuccessStatusCode());
        Assert.Equal(["en", "hi", "mr"], listed.EnumerateArray().Select(t => t.GetProperty("language").GetString()));
        Assert.Equal(hindi.GetProperty("translationGroupId").GetGuid(), marathi.GetProperty("translationGroupId").GetGuid());
    }

    [Fact]
    public async Task ASecondTranslationInTheSameLanguage_IsRefused_AndOneWithTheWrongNumberOfOptionsToo()
    {
        using var admin = await factory.AdminClientAsync();
        var source = await CreateQuestionAsync(admin, "Refused translation question?", "Right", "Wrong");

        (await admin.PostAsJsonAsync($"/v1/questions/{source}/translations", Translation("hi", "<p>पहला</p>", "सही", "गलत"))).EnsureSuccessStatusCode();

        var again = await admin.PostAsJsonAsync($"/v1/questions/{source}/translations", Translation("hi", "<p>दूसरा</p>", "सही", "गलत"));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("translation_exists", (await JsonAsync(again)).GetProperty("title").GetString());

        // Into the language it is already in, the source being the group's English question.
        var same = await admin.PostAsJsonAsync($"/v1/questions/{source}/translations", Translation("en", "<p>same</p>", "a", "b"));
        Assert.Equal(HttpStatusCode.Conflict, same.StatusCode);

        var third = await CreateQuestionAsync(admin, "Option count question?", "Right", "Wrong");
        var short_ = await admin.PostAsJsonAsync($"/v1/questions/{third}/translations", Translation("hi", "<p>एक</p>", "सही"));
        Assert.Equal(HttpStatusCode.BadRequest, short_.StatusCode);
    }

    [Fact]
    public async Task ATranslation_IsReviewedAndEditedOnItsOwn()
    {
        using var admin = await factory.AdminClientAsync();
        var source = await CreateQuestionAsync(admin, "Independent translation?", "Right", "Wrong");
        var translated = await JsonAsync((await admin.PostAsJsonAsync($"/v1/questions/{source}/translations", Translation("hi", "<p>स्वतंत्र</p>", "सही", "गलत"))).EnsureSuccessStatusCode());
        var translatedId = translated.GetProperty("id").GetGuid();

        (await admin.PostAsJsonAsync($"/v1/questions/{translatedId}/submit-for-review", new { })).EnsureSuccessStatusCode();

        var english = await JsonAsync((await admin.GetAsync($"/v1/questions/{source}")).EnsureSuccessStatusCode());
        Assert.Equal("draft", english.GetProperty("status").GetString());
        var hindi = await JsonAsync((await admin.GetAsync($"/v1/questions/{translatedId}")).EnsureSuccessStatusCode());
        Assert.Equal("in_review", hindi.GetProperty("status").GetString());
    }

    [Fact]
    public async Task TranslatingAQuestionThatIsNotThere_IsNotFound()
    {
        using var admin = await factory.AdminClientAsync();

        var response = await admin.PostAsJsonAsync($"/v1/questions/{Guid.NewGuid()}/translations", Translation("hi", "<p>x</p>", "क", "ख"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
