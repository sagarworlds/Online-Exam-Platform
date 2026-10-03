using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ExamPlatform.Modules.Identity.Domain.Rbac;
using ExamPlatform.Modules.QuestionBank.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// Drives the question bank over real HTTP and a real database (FR-5): authors create a multiple-choice
/// question with one correct option, read it back, and other modules can read it through the contract.
/// </summary>
public sealed class QuestionBankFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly object ValidQuestion = new
    {
        text = "What is 2 + 2?",
        options = new[]
        {
            new { text = "3", isCorrect = false },
            new { text = "4", isCorrect = true },
            new { text = "5", isCorrect = false },
        },
    };

    private async Task<HttpClient> ClientForAsync(string role)
    {
        var client = factory.CreateClient();
        var user = await factory.SignInAsAsync(role);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.AccessToken);
        return client;
    }

    [Theory]
    [InlineData(RbacCatalog.RoleNames.SuperAdmin)]
    [InlineData(RbacCatalog.RoleNames.ExamAdmin)]
    [InlineData(RbacCatalog.RoleNames.ContentAuthor)]
    public async Task Create_ThenGet_ReturnsTheStoredQuestionWithItsAnswerKey(string role)
    {
        using var client = await ClientForAsync(role);

        var created = await client.PostAsJsonAsync("/v1/questions", ValidQuestion);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("id").GetGuid();
        Assert.Equal($"/v1/questions/{id}", created.Headers.Location?.ToString());

        var fetched = await client.GetFromJsonAsync<JsonElement>($"/v1/questions/{id}");
        Assert.Equal("What is 2 + 2?", fetched.GetProperty("text").GetString());
        var options = fetched.GetProperty("options").EnumerateArray().ToList();
        Assert.Equal(["3", "4", "5"], options.Select(o => o.GetProperty("text").GetString()));
        Assert.Equal([false, true, false], options.Select(o => o.GetProperty("isCorrect").GetBoolean()));
    }

    [Fact]
    public async Task List_IncludesTheQuestionJustCreated()
    {
        using var client = await ClientForAsync(RbacCatalog.RoleNames.ExamAdmin);
        var created = await client.PostAsJsonAsync("/v1/questions", ValidQuestion);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var list = await client.GetFromJsonAsync<JsonElement>("/v1/questions");

        Assert.Contains(list.EnumerateArray(), q => q.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task List_SkipLeavesOutThatManyOfTheNewest_SoALaterPageContinuesTheSameOrder()
    {
        using var client = await ClientForAsync(RbacCatalog.RoleNames.ExamAdmin);
        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/v1/questions", ValidQuestion)).StatusCode);

        var firstPage = await IdsAsync(client, "/v1/questions");
        var afterTwo = await IdsAsync(client, "/v1/questions?skip=2");

        Assert.True(firstPage.Count >= 3);
        // Whatever else the shared database holds, the later page is the first one shifted by the two left out.
        Assert.Equal(firstPage.Skip(2).Take(firstPage.Count - 2), afterTwo.Take(firstPage.Count - 2));
    }

    [Fact]
    public async Task List_ANegativeSkipIsTreatedAsTheStart()
    {
        using var client = await ClientForAsync(RbacCatalog.RoleNames.ExamAdmin);
        await client.PostAsJsonAsync("/v1/questions", ValidQuestion);

        Assert.Equal(await IdsAsync(client, "/v1/questions"), await IdsAsync(client, "/v1/questions?skip=-5"));
    }

    private static async Task<List<Guid>> IdsAsync(HttpClient client, string url) =>
        (await client.GetFromJsonAsync<JsonElement>(url)).EnumerateArray().Select(q => q.GetProperty("id").GetGuid()).ToList();

    [Fact]
    public async Task OtherModules_ReadTheQuestionThroughTheContract_InOptionOrderWithTheKey()
    {
        using var client = await ClientForAsync(RbacCatalog.RoleNames.ExamAdmin);
        var created = await client.PostAsJsonAsync("/v1/questions", ValidQuestion);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var scope = factory.Services.CreateScope();
        var bank = scope.ServiceProvider.GetRequiredService<IQuestionBank>();
        var snapshots = await bank.GetAsync([id, Guid.NewGuid()], CancellationToken.None);

        var snapshot = Assert.Single(snapshots);
        Assert.Equal(id, snapshot.Id);
        Assert.Equal(["3", "4", "5"], snapshot.Options.Select(o => o.Text));
        Assert.Equal("4", Assert.Single(snapshot.Options, o => o.IsCorrect).Text);
    }

    [Theory]
    [InlineData("two correct")]
    [InlineData("no correct")]
    [InlineData("one option")]
    [InlineData("blank text")]
    [InlineData("empty markup")]
    [InlineData("script only")]
    public async Task Create_WithABrokenQuestion_Returns400InvalidQuestion(string scenario)
    {
        using var client = await ClientForAsync(RbacCatalog.RoleNames.ExamAdmin);
        object body = scenario switch
        {
            "two correct" => new { text = "Q?", options = new[] { new { text = "A", isCorrect = true }, new { text = "B", isCorrect = true } } },
            "no correct" => new { text = "Q?", options = new[] { new { text = "A", isCorrect = false }, new { text = "B", isCorrect = false } } },
            "one option" => new { text = "Q?", options = new[] { new { text = "A", isCorrect = true } } },
            "empty markup" => new { text = "<p><br></p>", options = new[] { new { text = "A", isCorrect = true }, new { text = "B", isCorrect = false } } },
            "script only" => new { text = "<script>alert(1)</script>", options = new[] { new { text = "A", isCorrect = true }, new { text = "B", isCorrect = false } } },
            _ => new { text = "  ", options = new[] { new { text = "A", isCorrect = true }, new { text = "B", isCorrect = false } } },
        };

        var response = await client.PostAsJsonAsync("/v1/questions", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_question", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Create_StoresTheSanitizedHtml_NotTheMarkupThatWasSent()
    {
        using var client = await ClientForAsync(RbacCatalog.RoleNames.ExamAdmin);

        var created = await client.PostAsJsonAsync("/v1/questions", new
        {
            text = "<p>Water is H<sub>2</sub>O <script>alert(1)</script><span onclick=\"alert(2)\">now</span><a href=\"javascript:alert(3)\"></a></p>",
            options = new[] { new { text = "A", isCorrect = true }, new { text = "B", isCorrect = false } },
        });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("id").GetGuid();
        // What is returned, and what is read back later, is the cleaned HTML: formatting kept, nothing executable.
        var expected = "<p>Water is H<sub>2</sub>O now</p>";
        Assert.Equal(expected, body.GetProperty("text").GetString());
        Assert.Equal(expected, (await client.GetFromJsonAsync<JsonElement>($"/v1/questions/{id}")).GetProperty("text").GetString());
    }

    private const string TinyPng =
        "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    [Fact]
    public async Task Create_WithAnEmbeddedPicture_StoresItAndReadsItBack()
    {
        using var client = await ClientForAsync(RbacCatalog.RoleNames.ExamAdmin);

        var created = await client.PostAsJsonAsync("/v1/questions", new
        {
            text = $"<p>Which shape?</p><p><img src=\"{TinyPng}\" alt=\"a shape\" onerror=\"alert(1)\"></p>",
            options = new[] { new { text = "Square", isCorrect = true }, new { text = "Circle", isCorrect = false } },
        });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var text = (await client.GetFromJsonAsync<JsonElement>($"/v1/questions/{id}")).GetProperty("text").GetString();
        Assert.Contains($"<img src=\"{TinyPng}\" alt=\"a shape\">", text);
        Assert.DoesNotContain("onerror", text);
    }

    [Theory]
    [InlineData("<p>x</p><img src=\"https://evil.example/track.png\">")]
    [InlineData("<p>x</p><img src=x onerror=alert(1)>")]
    [InlineData("<p>x</p><img src=\"data:image/svg+xml;base64,PHN2ZyBvbmxvYWQ9YWxlcnQoMSk+\">")]
    public async Task Create_WithAPictureThatIsNotAnEmbeddedPicture_Returns400AndStoresNothing(string text)
    {
        using var client = await ClientForAsync(RbacCatalog.RoleNames.ExamAdmin);

        var response = await client.PostAsJsonAsync("/v1/questions", new
        {
            text,
            options = new[] { new { text = "A", isCorrect = true }, new { text = "B", isCorrect = false } },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_question", body.GetProperty("title").GetString());
        Assert.Contains("image button", body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Create_WithPlainTextThatHasAngleBrackets_StoresThemAsLiteralText()
    {
        using var client = await ClientForAsync(RbacCatalog.RoleNames.ExamAdmin);

        var created = await client.PostAsJsonAsync("/v1/questions", new
        {
            text = "If a < b & b > c, which is largest?",
            options = new[] { new { text = "a", isCorrect = true }, new { text = "c", isCorrect = false } },
        });

        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("If a &lt; b &amp; b &gt; c, which is largest?", body.GetProperty("text").GetString());
    }

    [Fact]
    public async Task Create_WithoutAnOptionsList_Returns400NotACrash()
    {
        using var client = await ClientForAsync(RbacCatalog.RoleNames.ExamAdmin);

        var response = await client.PostAsJsonAsync("/v1/questions", new { text = "Q?" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_UnknownQuestion_Returns404()
    {
        using var client = await ClientForAsync(RbacCatalog.RoleNames.ExamAdmin);

        var response = await client.GetAsync($"/v1/questions/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(RbacCatalog.RoleNames.Candidate)]
    [InlineData(RbacCatalog.RoleNames.Guardian)]
    [InlineData(RbacCatalog.RoleNames.InstituteTeacher)]
    public async Task EveryRoute_WithoutTheAuthoringPermission_Returns403(string role)
    {
        using var client = await ClientForAsync(role);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/v1/questions", ValidQuestion)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/v1/questions")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/v1/questions/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/v1/questions/{Guid.NewGuid()}", ValidQuestion)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/v1/questions/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/v1/questions/placement", new { questionIds = new[] { Guid.NewGuid() }, chapterId = Guid.NewGuid() })).StatusCode);
    }

    [Fact]
    public async Task EveryRoute_WithoutAToken_Returns401()
    {
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/v1/questions", ValidQuestion)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/v1/questions")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutAsJsonAsync($"/v1/questions/{Guid.NewGuid()}", ValidQuestion)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.DeleteAsync($"/v1/questions/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/v1/questions/placement", new { questionIds = new[] { Guid.NewGuid() }, chapterId = Guid.NewGuid() })).StatusCode);
    }
}
