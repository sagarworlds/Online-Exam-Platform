using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ExamPlatform.Modules.Identity.Domain.Rbac;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// Classes, the level above books, over real HTTP and a real database: a class such as the 4th holds books ("English" for the 4th is
/// not "English" for the 5th), a book keeps its chapters and questions, and an exam limited to a book or to chosen chapters of it is
/// in effect an exam for that class. Classes are archived, never deleted.
/// </summary>
public sealed class ClassFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<HttpClient> AuthorAsync()
    {
        var client = factory.CreateClient();
        var user = await factory.SignInAsAsync(RbacCatalog.RoleNames.ContentAuthor);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.AccessToken);
        return client;
    }

    private static string Unique(string prefix) => $"{prefix} {Guid.NewGuid():N}";

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await JsonAsync(response)).GetProperty("title").GetString());
    }

    private static async Task<JsonElement> CreateClassAsync(HttpClient client, string? name = null)
    {
        var response = await client.PostAsJsonAsync("/v1/classes", new { name = name ?? Unique("Class") });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await JsonAsync(response);
    }

    private static async Task<JsonElement> CreateBookAsync(HttpClient client, string name, Guid? classId)
    {
        var response = await client.PostAsJsonAsync("/v1/books", new { name, subject = "English", classId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await JsonAsync(response);
    }

    private static async Task<JsonElement> AddChapterAsync(HttpClient client, Guid bookId, string title) =>
        await JsonAsync((await client.PostAsJsonAsync($"/v1/books/{bookId}/chapters", new { title })).EnsureSuccessStatusCode());

    private static Guid ChapterIdOf(JsonElement book, string title) =>
        book.GetProperty("chapters").EnumerateArray().Single(c => c.GetProperty("title").GetString() == title).GetProperty("id").GetGuid();

    private static async Task<JsonElement> CreateQuestionAsync(HttpClient client, string text, Guid? chapterId) =>
        await JsonAsync((await client.PostAsJsonAsync("/v1/questions", new
        {
            text,
            chapterId,
            options = new[] { new { text = "A", isCorrect = true }, new { text = "B", isCorrect = false } },
        })).EnsureSuccessStatusCode());

    private static async Task<IReadOnlyList<JsonElement>> ClassesAsync(HttpClient client, bool includeArchived = false) =>
        (await JsonAsync(await client.GetAsync($"/v1/classes?includeArchived={includeArchived.ToString().ToLowerInvariant()}")))
            .EnumerateArray().ToList();

    // ---- the classes themselves -------------------------------------------------------------------

    [Fact]
    public async Task AClass_IsCreatedRenamedArchivedAndRestored_AndHiddenFromTheDefaultListWhileArchived()
    {
        using var client = await AuthorAsync();
        var created = await CreateClassAsync(client, Unique("4th"));
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal(0, created.GetProperty("bookCount").GetInt32());
        Assert.False(created.GetProperty("isArchived").GetBoolean());
        Assert.Contains(await ClassesAsync(client), c => c.GetProperty("id").GetGuid() == id);

        var renamedTo = Unique("Class 4");
        var renamed = await JsonAsync(await client.PutAsJsonAsync($"/v1/classes/{id}", new { name = $"  {renamedTo}  " }));
        Assert.Equal(renamedTo, renamed.GetProperty("name").GetString());

        var archived = await JsonAsync(await client.PostAsync($"/v1/classes/{id}/archive", null));
        Assert.True(archived.GetProperty("isArchived").GetBoolean());
        Assert.DoesNotContain(await ClassesAsync(client), c => c.GetProperty("id").GetGuid() == id);
        Assert.Contains(await ClassesAsync(client, includeArchived: true), c => c.GetProperty("id").GetGuid() == id);

        var restored = await JsonAsync(await client.PostAsync($"/v1/classes/{id}/restore", null));
        Assert.False(restored.GetProperty("isArchived").GetBoolean());
        Assert.Contains(await ClassesAsync(client), c => c.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task ANameThatIsTaken_IsRefused_IgnoringCase_OnCreateAndOnRename()
    {
        using var client = await AuthorAsync();
        var name = Unique("Grade");
        await CreateClassAsync(client, name);
        var other = (await CreateClassAsync(client)).GetProperty("id").GetGuid();

        await AssertProblemAsync(await client.PostAsJsonAsync("/v1/classes", new { name = name.ToUpperInvariant() }), HttpStatusCode.Conflict, "duplicate_class");
        await AssertProblemAsync(await client.PutAsJsonAsync($"/v1/classes/{other}", new { name = $" {name.ToLowerInvariant()} " }), HttpStatusCode.Conflict, "duplicate_class");
    }

    [Fact]
    public async Task AClass_CanBeRenamedToItsOwnName_WithADifferentCase()
    {
        using var client = await AuthorAsync();
        var name = Unique("Fifth");
        var id = (await CreateClassAsync(client, name)).GetProperty("id").GetGuid();

        var response = await client.PutAsJsonAsync($"/v1/classes/{id}", new { name = name.ToUpperInvariant() });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(name.ToUpperInvariant(), (await JsonAsync(response)).GetProperty("name").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ABlankName_IsRefused(string name)
    {
        using var client = await AuthorAsync();

        await AssertProblemAsync(await client.PostAsJsonAsync("/v1/classes", new { name }), HttpStatusCode.BadRequest, "invalid_class");
    }

    [Fact]
    public async Task ATooLongName_IsRefused()
    {
        using var client = await AuthorAsync();

        await AssertProblemAsync(
            await client.PostAsJsonAsync("/v1/classes", new { name = new string('x', 101) }), HttpStatusCode.BadRequest, "invalid_class");
    }

    [Fact]
    public async Task ChangingAClassThatDoesNotExist_Returns404()
    {
        using var client = await AuthorAsync();
        var unknown = Guid.NewGuid();

        await AssertProblemAsync(await client.PutAsJsonAsync($"/v1/classes/{unknown}", new { name = "x" }), HttpStatusCode.NotFound, "class_not_found");
        await AssertProblemAsync(await client.PostAsync($"/v1/classes/{unknown}/archive", null), HttpStatusCode.NotFound, "class_not_found");
        await AssertProblemAsync(await client.PostAsync($"/v1/classes/{unknown}/restore", null), HttpStatusCode.NotFound, "class_not_found");
    }

    // ---- books under a class ----------------------------------------------------------------------

    [Fact]
    public async Task TheSameBookNameUnderTwoClasses_IsTwoBooks_EachReportingItsClass()
    {
        using var client = await AuthorAsync();
        var fourth = await CreateClassAsync(client, Unique("4th"));
        var fifth = await CreateClassAsync(client, Unique("5th"));
        var fourthId = fourth.GetProperty("id").GetGuid();
        var fifthId = fifth.GetProperty("id").GetGuid();

        var englishFourth = await CreateBookAsync(client, "English", fourthId);
        var englishFifth = await CreateBookAsync(client, "English", fifthId);

        Assert.NotEqual(englishFourth.GetProperty("id").GetGuid(), englishFifth.GetProperty("id").GetGuid());
        Assert.Equal(fourthId, englishFourth.GetProperty("classId").GetGuid());
        Assert.Equal(fourth.GetProperty("name").GetString(), englishFourth.GetProperty("className").GetString());

        var fetched = await JsonAsync(await client.GetAsync($"/v1/books/{englishFifth.GetProperty("id").GetGuid()}"));
        Assert.Equal(fifth.GetProperty("name").GetString(), fetched.GetProperty("className").GetString());

        var list = await JsonAsync(await client.GetAsync("/v1/books"));
        var mine = list.EnumerateArray().Where(b => b.GetProperty("classId").ValueKind == JsonValueKind.String
            && (b.GetProperty("classId").GetGuid() == fourthId || b.GetProperty("classId").GetGuid() == fifthId)).ToList();
        Assert.Equal(2, mine.Count);

        // Each class reports how many books it holds.
        Assert.All(
            (await ClassesAsync(client)).Where(c => c.GetProperty("id").GetGuid() == fourthId || c.GetProperty("id").GetGuid() == fifthId),
            c => Assert.Equal(1, c.GetProperty("bookCount").GetInt32()));
    }

    [Fact]
    public async Task ABook_WithNoClass_ReportsNone_AsEveryBookDidBeforeClassesExisted()
    {
        using var client = await AuthorAsync();

        var book = await CreateBookAsync(client, Unique("General Knowledge"), classId: null);

        Assert.Equal(JsonValueKind.Null, book.GetProperty("classId").ValueKind);
        Assert.Equal(JsonValueKind.Null, book.GetProperty("className").ValueKind);
    }

    [Fact]
    public async Task ABook_CanBeMovedToAnotherClass_AndTakenOutOfItsClass()
    {
        using var client = await AuthorAsync();
        var fourth = (await CreateClassAsync(client, Unique("4th"))).GetProperty("id").GetGuid();
        var fifth = await CreateClassAsync(client, Unique("5th"));
        var book = await CreateBookAsync(client, "English", fourth);
        var bookId = book.GetProperty("id").GetGuid();

        var moved = await JsonAsync(await client.PutAsJsonAsync(
            $"/v1/books/{bookId}", new { name = "English", subject = "English", classId = fifth.GetProperty("id").GetGuid() }));
        Assert.Equal(fifth.GetProperty("name").GetString(), moved.GetProperty("className").GetString());

        // The details are replaced as a whole, so a request that names no class takes the book out of its class.
        var free = await JsonAsync(await client.PutAsJsonAsync($"/v1/books/{bookId}", new { name = "English", subject = "English" }));
        Assert.Equal(JsonValueKind.Null, free.GetProperty("classId").ValueKind);
    }

    [Fact]
    public async Task ABook_UnderAClassThatDoesNotExist_Returns404()
    {
        using var client = await AuthorAsync();

        await AssertProblemAsync(
            await client.PostAsJsonAsync("/v1/books", new { name = "English", classId = Guid.NewGuid() }), HttpStatusCode.NotFound, "class_not_found");
    }

    [Fact]
    public async Task AnArchivedClass_TakesNoNewBooks_ButItsOwnBooksStayAndCanBeEdited()
    {
        using var client = await AuthorAsync();
        var classId = (await CreateClassAsync(client, Unique("3rd"))).GetProperty("id").GetGuid();
        var other = (await CreateClassAsync(client, Unique("4th"))).GetProperty("id").GetGuid();
        var inside = await CreateBookAsync(client, "English", classId);
        var outside = await CreateBookAsync(client, "Maths", other);
        (await client.PostAsync($"/v1/classes/{classId}/archive", null)).EnsureSuccessStatusCode();

        // No new book can be put under it, whether made new or moved there.
        await AssertProblemAsync(
            await client.PostAsJsonAsync("/v1/books", new { name = "Science", classId }), HttpStatusCode.Conflict, "class_archived");
        await AssertProblemAsync(
            await client.PutAsJsonAsync($"/v1/books/{outside.GetProperty("id").GetGuid()}", new { name = "Maths", classId }),
            HttpStatusCode.Conflict,
            "class_archived");

        // The book already under it keeps its class, and can still be renamed there.
        var renamed = await client.PutAsJsonAsync($"/v1/books/{inside.GetProperty("id").GetGuid()}", new { name = "English 2", subject = "English", classId });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal(classId, (await JsonAsync(renamed)).GetProperty("classId").GetGuid());
    }

    // ---- questions and exams ----------------------------------------------------------------------

    [Fact]
    public async Task Questions_AreFilteredByClass_AndReportTheClassTheyAreFiledUnder()
    {
        using var client = await AuthorAsync();
        var fourth = await CreateClassAsync(client, Unique("4th"));
        var fifth = await CreateClassAsync(client, Unique("5th"));
        var fourthId = fourth.GetProperty("id").GetGuid();
        var fifthId = fifth.GetProperty("id").GetGuid();

        var english4 = await AddChapterAsync(client, (await CreateBookAsync(client, "English", fourthId)).GetProperty("id").GetGuid(), "Nouns");
        var english5 = await AddChapterAsync(client, (await CreateBookAsync(client, "English", fifthId)).GetProperty("id").GetGuid(), "Verbs");
        var classless = await AddChapterAsync(client, (await CreateBookAsync(client, Unique("Loose"), classId: null)).GetProperty("id").GetGuid(), "Misc");

        var inFourth = await CreateQuestionAsync(client, Unique("Which is a noun?"), ChapterIdOf(english4, "Nouns"));
        var inFifth = await CreateQuestionAsync(client, Unique("Which is a verb?"), ChapterIdOf(english5, "Verbs"));
        var inClassless = await CreateQuestionAsync(client, Unique("Miscellany"), ChapterIdOf(classless, "Misc"));
        var unfiled = await CreateQuestionAsync(client, Unique("Unfiled"), chapterId: null);

        // What a question reports about where it is filed, in the response that made it and when read back.
        Assert.Equal(fourthId, inFourth.GetProperty("classId").GetGuid());
        Assert.Equal(fourth.GetProperty("name").GetString(), inFourth.GetProperty("className").GetString());
        Assert.Equal("English", inFourth.GetProperty("bookName").GetString());
        Assert.Equal("Nouns", inFourth.GetProperty("chapterTitle").GetString());
        Assert.Equal(JsonValueKind.Null, inClassless.GetProperty("classId").ValueKind);
        Assert.Equal(JsonValueKind.Null, unfiled.GetProperty("className").ValueKind);

        var listed = await JsonAsync(await client.GetAsync($"/v1/questions?classId={fourthId}"));
        var ids = listed.EnumerateArray().Select(q => q.GetProperty("id").GetGuid()).ToList();
        Assert.Equal([inFourth.GetProperty("id").GetGuid()], ids);
        Assert.Equal(fourth.GetProperty("name").GetString(), listed[0].GetProperty("className").GetString());

        var fifthIds = (await JsonAsync(await client.GetAsync($"/v1/questions?classId={fifthId}")))
            .EnumerateArray().Select(q => q.GetProperty("id").GetGuid()).ToList();
        Assert.Equal([inFifth.GetProperty("id").GetGuid()], fifthIds);

        // The class limit combines with the book limit: a book of the other class matches nothing.
        var mismatch = await JsonAsync(await client.GetAsync(
            $"/v1/questions?classId={fourthId}&bookId={english5.GetProperty("id").GetGuid()}"));
        Assert.Empty(mismatch.EnumerateArray());
    }

    [Fact]
    public async Task AnExamLimitedToABook_OrToItsChapters_ReportsTheClassOfThatBook()
    {
        // Making an exam needs the exam-authoring permission, which a content author does not hold.
        using var client = await factory.AdminClientAsync();
        var fourth = await CreateClassAsync(client, Unique("4th"));
        var book = await CreateBookAsync(client, "English", fourth.GetProperty("id").GetGuid());
        var bookId = book.GetProperty("id").GetGuid();
        var withChapter = await AddChapterAsync(client, bookId, "Nouns");
        var className = fourth.GetProperty("name").GetString();

        async Task<JsonElement> ExamWithScopeAsync(object scope) =>
            (await JsonAsync((await client.PostAsJsonAsync("/v1/exams", new { name = "Olympiad English", scope })).EnsureSuccessStatusCode()))
            .GetProperty("scope");

        var wholeBook = await ExamWithScopeAsync(new { type = "Book", bookId });
        Assert.Equal(className, wholeBook.GetProperty("className").GetString());
        Assert.Equal("English", wholeBook.GetProperty("bookName").GetString());

        var chapters = await ExamWithScopeAsync(new { type = "Chapters", bookId, chapterIds = new[] { ChapterIdOf(withChapter, "Nouns") } });
        Assert.Equal(className, chapters.GetProperty("className").GetString());

        var anywhere = await ExamWithScopeAsync(new { type = "Independent" });
        Assert.Equal(JsonValueKind.Null, anywhere.GetProperty("className").ValueKind);

        var classless = await CreateBookAsync(client, Unique("Loose"), classId: null);
        var looseScope = await ExamWithScopeAsync(new { type = "Book", bookId = classless.GetProperty("id").GetGuid() });
        Assert.Equal(JsonValueKind.Null, looseScope.GetProperty("className").ValueKind);
    }
}
