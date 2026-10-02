using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ExamPlatform.Modules.Identity.Domain.Rbac;
using ExamPlatform.Modules.QuestionBank.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// Drives books and chapters over real HTTP and a real database (FR-5): authors create a book with chapters, file
/// questions under a chapter, and list the bank by book, chapter or "not filed".
/// </summary>
public sealed class BookFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<HttpClient> ClientForAsync(string role)
    {
        var client = factory.CreateClient();
        var user = await factory.SignInAsAsync(role);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.AccessToken);
        return client;
    }

    private static string Unique(string prefix) => $"{prefix} {Guid.NewGuid():N}"[..Math.Min(40, prefix.Length + 9)];

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<JsonElement> CreateBookAsync(HttpClient client, string? name = null, string? subject = "Maths")
    {
        var response = await client.PostAsJsonAsync("/v1/books", new { name = name ?? Unique("Book"), subject });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await JsonAsync(response);
    }

    private static async Task<JsonElement> AddChapterAsync(HttpClient client, Guid bookId, string title)
    {
        var response = await client.PostAsJsonAsync($"/v1/books/{bookId}/chapters", new { title });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await JsonAsync(response);
    }

    private static Guid ChapterIdOf(JsonElement book, string title) =>
        book.GetProperty("chapters").EnumerateArray().Single(c => c.GetProperty("title").GetString() == title).GetProperty("id").GetGuid();

    private static async Task<JsonElement> CreateQuestionAsync(HttpClient client, string text, Guid? chapterId = null)
    {
        var response = await client.PostAsJsonAsync("/v1/questions", new
        {
            text,
            chapterId,
            options = new[] { new { text = "A", isCorrect = true }, new { text = "B", isCorrect = false } },
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await JsonAsync(response);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await JsonAsync(response)).GetProperty("title").GetString());
    }

    [Theory]
    [InlineData(RbacCatalog.RoleNames.SuperAdmin)]
    [InlineData(RbacCatalog.RoleNames.ExamAdmin)]
    [InlineData(RbacCatalog.RoleNames.ContentAuthor)]
    public async Task AuthorsCanCreateABookAddChaptersAndReadItBack(string role)
    {
        using var client = await ClientForAsync(role);

        var book = await CreateBookAsync(client);
        var bookId = book.GetProperty("id").GetGuid();
        await AddChapterAsync(client, bookId, "Algebra");
        await AddChapterAsync(client, bookId, "Geometry");

        var fetched = await client.GetFromJsonAsync<JsonElement>($"/v1/books/{bookId}");
        Assert.Equal("Maths", fetched.GetProperty("subject").GetString());
        Assert.False(fetched.GetProperty("isArchived").GetBoolean());
        var chapters = fetched.GetProperty("chapters").EnumerateArray().ToList();
        Assert.Equal(["Algebra", "Geometry"], chapters.Select(c => c.GetProperty("title").GetString()));
        Assert.Equal([1, 2], chapters.Select(c => c.GetProperty("order").GetInt32()));
        Assert.Equal([0, 0], chapters.Select(c => c.GetProperty("questionCount").GetInt32()));
    }

    [Fact]
    public async Task List_ReturnsBooksWithTheirChapters_AndHidesArchivedOnesUnlessAsked()
    {
        using var client = await ClientForAsync(RbacCatalog.RoleNames.ExamAdmin);
        var open = await CreateBookAsync(client);
        var archived = await CreateBookAsync(client);
        (await client.PostAsync($"/v1/books/{archived.GetProperty("id").GetGuid()}/archive", null)).EnsureSuccessStatusCode();

        var visible = (await client.GetFromJsonAsync<JsonElement>("/v1/books")).EnumerateArray().Select(b => b.GetProperty("id").GetGuid()).ToList();
        var all = (await client.GetFromJsonAsync<JsonElement>("/v1/books?includeArchived=true")).EnumerateArray().Select(b => b.GetProperty("id").GetGuid()).ToList();

        Assert.Contains(open.GetProperty("id").GetGuid(), visible);
        Assert.DoesNotContain(archived.GetProperty("id").GetGuid(), visible);
        Assert.Contains(archived.GetProperty("id").GetGuid(), all);
    }

    [Fact]
    public async Task ABookCanBeRenamed_ArchivedAndRestored()
    {
        using var client = await ClientForAsync(RbacCatalog.RoleNames.ExamAdmin);
        var bookId = (await CreateBookAsync(client)).GetProperty("id").GetGuid();

        var renamed = await JsonAsync(await client.PutAsJsonAsync($"/v1/books/{bookId}", new { name = "Physics Grade 9", subject = "Physics", description = "Motion" }));
        Assert.Equal("Physics Grade 9", renamed.GetProperty("name").GetString());
        Assert.Equal("Motion", renamed.GetProperty("description").GetString());

        Assert.True((await JsonAsync(await client.PostAsync($"/v1/books/{bookId}/archive", null))).GetProperty("isArchived").GetBoolean());
        Assert.False((await JsonAsync(await client.PostAsync($"/v1/books/{bookId}/restore", null))).GetProperty("isArchived").GetBoolean());
    }

    [Fact]
    public async Task AChapterCanBeRenamed_ArchivedAndRestored()
    {
        using var client = await ClientForAsync(RbacCatalog.RoleNames.ExamAdmin);
        var book = await CreateBookAsync(client);
        var bookId = book.GetProperty("id").GetGuid();
        var chapterId = ChapterIdOf(await AddChapterAsync(client, bookId, "Algbra"), "Algbra");

        var renamed = await JsonAsync(await client.PutAsJsonAsync($"/v1/books/{bookId}/chapters/{chapterId}", new { title = "Algebra" }));
        Assert.Equal("Algebra", renamed.GetProperty("chapters")[0].GetProperty("title").GetString());

        var archived = await JsonAsync(await client.PostAsync($"/v1/books/{bookId}/chapters/{chapterId}/archive", null));
        Assert.True(archived.GetProperty("chapters")[0].GetProperty("isArchived").GetBoolean());
        var restored = await JsonAsync(await client.PostAsync($"/v1/books/{bookId}/chapters/{chapterId}/restore", null));
        Assert.False(restored.GetProperty("chapters")[0].GetProperty("isArchived").GetBoolean());
    }

    [Theory]
    [InlineData("blank book name")]
    [InlineData("blank chapter title")]
    public async Task ABlankNameOrTitle_Returns400InvalidBook(string scenario)
    {
        using var client = await ClientForAsync(RbacCatalog.RoleNames.ExamAdmin);

        if (scenario == "blank book name")
        {
            await AssertProblemAsync(await client.PostAsJsonAsync("/v1/books", new { name = "  " }), HttpStatusCode.BadRequest, "invalid_book");
            return;
        }

        var bookId = (await CreateBookAsync(client)).GetProperty("id").GetGuid();
        await AssertProblemAsync(await client.PostAsJsonAsync($"/v1/books/{bookId}/chapters", new { title = "" }), HttpStatusCode.BadRequest, "invalid_book");
    }

    [Fact]
    public async Task ADuplicateChapterTitle_Returns409_AndOneForAnArchivedBookReturns409()
    {
        using var client = await ClientForAsync(RbacCatalog.RoleNames.ExamAdmin);
        var bookId = (await CreateBookAsync(client)).GetProperty("id").GetGuid();
        await AddChapterAsync(client, bookId, "Algebra");

        await AssertProblemAsync(await client.PostAsJsonAsync($"/v1/books/{bookId}/chapters", new { title = "ALGEBRA" }), HttpStatusCode.Conflict, "duplicate_chapter");

        await client.PostAsync($"/v1/books/{bookId}/archive", null);
        await AssertProblemAsync(await client.PostAsJsonAsync($"/v1/books/{bookId}/chapters", new { title = "Geometry" }), HttpStatusCode.Conflict, "book_archived");
    }

    [Fact]
    public async Task UnknownBooksAndChapters_Return404()
    {
        using var client = await ClientForAsync(RbacCatalog.RoleNames.ExamAdmin);
        var bookId = (await CreateBookAsync(client)).GetProperty("id").GetGuid();

        await AssertProblemAsync(await client.GetAsync($"/v1/books/{Guid.NewGuid()}"), HttpStatusCode.NotFound, "book_not_found");
        await AssertProblemAsync(await client.PutAsJsonAsync($"/v1/books/{Guid.NewGuid()}/chapters/{Guid.NewGuid()}", new { title = "x" }), HttpStatusCode.NotFound, "book_not_found");
        await AssertProblemAsync(await client.PutAsJsonAsync($"/v1/books/{bookId}/chapters/{Guid.NewGuid()}", new { title = "x" }), HttpStatusCode.NotFound, "chapter_not_found");
    }

    [Fact]
    public async Task AQuestionFiledUnderAChapter_ComesBackWithItsBookAndChapter_AndIsCounted()
    {
        using var client = await ClientForAsync(RbacCatalog.RoleNames.ExamAdmin);
        var book = await CreateBookAsync(client, "Maths Grade 10");
        var bookId = book.GetProperty("id").GetGuid();
        var chapterId = ChapterIdOf(await AddChapterAsync(client, bookId, "Algebra"), "Algebra");

        var question = await CreateQuestionAsync(client, "Solve x + 1 = 2", chapterId);
        var questionId = question.GetProperty("id").GetGuid();

        Assert.Equal(chapterId, question.GetProperty("chapterId").GetGuid());
        Assert.Equal("Algebra", question.GetProperty("chapterTitle").GetString());
        Assert.Equal(bookId, question.GetProperty("bookId").GetGuid());
        Assert.Equal("Maths Grade 10", question.GetProperty("bookName").GetString());
        var fetched = await client.GetFromJsonAsync<JsonElement>($"/v1/questions/{questionId}");
        Assert.Equal("Algebra", fetched.GetProperty("chapterTitle").GetString());
        Assert.Equal(1, (await client.GetFromJsonAsync<JsonElement>($"/v1/books/{bookId}")).GetProperty("chapters")[0].GetProperty("questionCount").GetInt32());
    }

    [Fact]
    public async Task TheQuestionListCanBeNarrowedToABookAChapterOrToQuestionsNotFiled()
    {
        using var client = await ClientForAsync(RbacCatalog.RoleNames.ExamAdmin);
        var bookA = (await CreateBookAsync(client)).GetProperty("id").GetGuid();
        var bookB = (await CreateBookAsync(client)).GetProperty("id").GetGuid();
        var algebra = ChapterIdOf(await AddChapterAsync(client, bookA, "Algebra"), "Algebra");
        var geometry = ChapterIdOf(await AddChapterAsync(client, bookA, "Geometry"), "Geometry");
        var other = ChapterIdOf(await AddChapterAsync(client, bookB, "Optics"), "Optics");
        var inAlgebra = (await CreateQuestionAsync(client, "In algebra", algebra)).GetProperty("id").GetGuid();
        var inGeometry = (await CreateQuestionAsync(client, "In geometry", geometry)).GetProperty("id").GetGuid();
        var inOptics = (await CreateQuestionAsync(client, "In optics", other)).GetProperty("id").GetGuid();
        var unfiled = (await CreateQuestionAsync(client, "Not filed")).GetProperty("id").GetGuid();

        async Task<List<Guid>> IdsAsync(string query) =>
            (await client.GetFromJsonAsync<JsonElement>($"/v1/questions{query}")).EnumerateArray().Select(q => q.GetProperty("id").GetGuid()).ToList();

        var byBook = await IdsAsync($"?bookId={bookA}");
        Assert.Equal([inAlgebra, inGeometry], byBook.OrderBy(id => id == inAlgebra ? 0 : 1));
        Assert.DoesNotContain(inOptics, byBook);
        Assert.DoesNotContain(unfiled, byBook);

        Assert.Equal([inGeometry], await IdsAsync($"?chapterId={geometry}"));
        Assert.Equal([inOptics], await IdsAsync($"?bookId={bookB}&chapterId={other}"));
        Assert.Empty(await IdsAsync($"?bookId={bookB}&chapterId={algebra}"));

        var unfiledList = await IdsAsync("?unfiled=true");
        Assert.Contains(unfiled, unfiledList);
        Assert.DoesNotContain(inAlgebra, unfiledList);
    }

    [Fact]
    public async Task FilingUnderAChapterThatIsMissingOrArchived_IsRefusedAndStoresNothing()
    {
        using var client = await ClientForAsync(RbacCatalog.RoleNames.ExamAdmin);
        var bookId = (await CreateBookAsync(client)).GetProperty("id").GetGuid();
        var chapterId = ChapterIdOf(await AddChapterAsync(client, bookId, "Algebra"), "Algebra");
        object Body(Guid chapter) => new { text = "Q", chapterId = chapter, options = new[] { new { text = "A", isCorrect = true }, new { text = "B", isCorrect = false } } };

        await AssertProblemAsync(await client.PostAsJsonAsync("/v1/questions", Body(Guid.NewGuid())), HttpStatusCode.NotFound, "chapter_not_found");

        await client.PostAsync($"/v1/books/{bookId}/chapters/{chapterId}/archive", null);
        await AssertProblemAsync(await client.PostAsJsonAsync("/v1/questions", Body(chapterId)), HttpStatusCode.Conflict, "book_archived");

        await client.PostAsync($"/v1/books/{bookId}/chapters/{chapterId}/restore", null);
        await client.PostAsync($"/v1/books/{bookId}/archive", null);
        await AssertProblemAsync(await client.PostAsJsonAsync("/v1/questions", Body(chapterId)), HttpStatusCode.Conflict, "book_archived");

        Assert.Empty((await client.GetFromJsonAsync<JsonElement>($"/v1/questions?chapterId={chapterId}")).EnumerateArray());
    }

    [Fact]
    public async Task ArchivingAChapter_KeepsItsQuestions()
    {
        using var client = await ClientForAsync(RbacCatalog.RoleNames.ExamAdmin);
        var bookId = (await CreateBookAsync(client)).GetProperty("id").GetGuid();
        var chapterId = ChapterIdOf(await AddChapterAsync(client, bookId, "Algebra"), "Algebra");
        var questionId = (await CreateQuestionAsync(client, "Kept", chapterId)).GetProperty("id").GetGuid();

        await client.PostAsync($"/v1/books/{bookId}/chapters/{chapterId}/archive", null);

        var question = await client.GetFromJsonAsync<JsonElement>($"/v1/questions/{questionId}");
        Assert.Equal(chapterId, question.GetProperty("chapterId").GetGuid());
        Assert.Equal(1, (await client.GetFromJsonAsync<JsonElement>($"/v1/books/{bookId}")).GetProperty("chapters")[0].GetProperty("questionCount").GetInt32());
    }

    [Fact]
    public async Task OtherModules_SeeWhichBookAndChapterAQuestionIsFiledUnder()
    {
        using var client = await ClientForAsync(RbacCatalog.RoleNames.ExamAdmin);
        var bookId = (await CreateBookAsync(client)).GetProperty("id").GetGuid();
        var chapterId = ChapterIdOf(await AddChapterAsync(client, bookId, "Algebra"), "Algebra");
        var filed = (await CreateQuestionAsync(client, "Filed", chapterId)).GetProperty("id").GetGuid();
        var unfiled = (await CreateQuestionAsync(client, "Unfiled")).GetProperty("id").GetGuid();

        using var scope = factory.Services.CreateScope();
        var snapshots = await scope.ServiceProvider.GetRequiredService<IQuestionBank>().GetAsync([filed, unfiled], CancellationToken.None);

        var filedSnapshot = Assert.Single(snapshots, s => s.Id == filed);
        Assert.Equal(chapterId, filedSnapshot.ChapterId);
        Assert.Equal(bookId, filedSnapshot.BookId);
        var unfiledSnapshot = Assert.Single(snapshots, s => s.Id == unfiled);
        Assert.Null(unfiledSnapshot.ChapterId);
        Assert.Null(unfiledSnapshot.BookId);
    }

    [Theory]
    [InlineData(RbacCatalog.RoleNames.Candidate)]
    [InlineData(RbacCatalog.RoleNames.Guardian)]
    [InlineData(RbacCatalog.RoleNames.InstituteTeacher)]
    public async Task EveryBookRoute_WithoutTheAuthoringPermission_Returns403(string role)
    {
        using var client = await ClientForAsync(role);
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/v1/books", new { name = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/v1/books")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/v1/books/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/v1/books/{id}", new { name = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/v1/books/{id}/archive", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/v1/books/{id}/chapters", new { title = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/v1/books/{id}/chapters/{id}", new { title = "x" })).StatusCode);
    }

    [Fact]
    public async Task TwoChaptersAddedAtTheSameMoment_BothLandOrOneGetsAConflictToRetry()
    {
        using var client = await ClientForAsync(RbacCatalog.RoleNames.ExamAdmin);
        var bookId = (await CreateBookAsync(client)).GetProperty("id").GetGuid();

        var responses = await Task.WhenAll(Enumerable.Range(1, 6).Select(i => client.PostAsJsonAsync($"/v1/books/{bookId}/chapters", new { title = $"Chapter {i}" })));

        // Never a server error: each request either succeeds or is told to retry.
        Assert.All(responses, r => Assert.True(r.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict, $"unexpected {(int)r.StatusCode}"));
        var stored = (await client.GetFromJsonAsync<JsonElement>($"/v1/books/{bookId}")).GetProperty("chapters").EnumerateArray().ToList();
        Assert.Equal(responses.Count(r => r.StatusCode == HttpStatusCode.Created), stored.Count);
        Assert.Equal(stored.Count, stored.Select(c => c.GetProperty("order").GetInt32()).Distinct().Count());
    }
}
