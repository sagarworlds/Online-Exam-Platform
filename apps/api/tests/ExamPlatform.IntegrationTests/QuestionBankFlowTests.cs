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
    public async Task Create_WithABrokenQuestion_Returns400InvalidQuestion(string scenario)
    {
        using var client = await ClientForAsync(RbacCatalog.RoleNames.ExamAdmin);
        object body = scenario switch
        {
            "two correct" => new { text = "Q?", options = new[] { new { text = "A", isCorrect = true }, new { text = "B", isCorrect = true } } },
            "no correct" => new { text = "Q?", options = new[] { new { text = "A", isCorrect = false }, new { text = "B", isCorrect = false } } },
            "one option" => new { text = "Q?", options = new[] { new { text = "A", isCorrect = true } } },
            _ => new { text = "  ", options = new[] { new { text = "A", isCorrect = true }, new { text = "B", isCorrect = false } } },
        };

        var response = await client.PostAsJsonAsync("/v1/questions", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_question", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
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
    }

    [Fact]
    public async Task EveryRoute_WithoutAToken_Returns401()
    {
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/v1/questions", ValidQuestion)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/v1/questions")).StatusCode);
    }
}
