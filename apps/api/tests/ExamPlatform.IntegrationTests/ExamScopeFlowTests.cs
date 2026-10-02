using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ExamPlatform.Modules.Identity.Domain.Rbac;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// Exams limited to a book or to chosen chapters (FR-11), over real HTTP and a real database: the API refuses a question
/// outside the exam's scope, however the request is made, and reports the scope with the names of the book and chapters.
/// </summary>
public sealed class ExamScopeFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record Shelf(Guid BookId, Guid Algebra, Guid Geometry, Guid InAlgebra, Guid InGeometry, Guid Unfiled);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await JsonAsync(response)).GetProperty("title").GetString());
    }

    /// <summary>A book with two chapters, a question in each, and one question that is not filed anywhere.</summary>
    private static async Task<Shelf> ShelfAsync(HttpClient admin, string name = "Maths")
    {
        var book = await JsonAsync((await admin.PostAsJsonAsync("/v1/books", new { name = $"{name} {Guid.NewGuid():N}", subject = "Maths" })).EnsureSuccessStatusCode());
        var bookId = book.GetProperty("id").GetGuid();
        await admin.PostAsJsonAsync($"/v1/books/{bookId}/chapters", new { title = "Algebra" });
        var withChapters = await JsonAsync(await admin.PostAsJsonAsync($"/v1/books/{bookId}/chapters", new { title = "Geometry" }));
        Guid Chapter(string title) => withChapters.GetProperty("chapters").EnumerateArray().Single(c => c.GetProperty("title").GetString() == title).GetProperty("id").GetGuid();
        var algebra = Chapter("Algebra");
        var geometry = Chapter("Geometry");

        async Task<Guid> Question(string text, Guid? chapter) =>
            (await JsonAsync((await admin.PostAsJsonAsync("/v1/questions", new
            {
                text,
                chapterId = chapter,
                options = new[] { new { text = "Right", isCorrect = true }, new { text = "Wrong", isCorrect = false } },
            })).EnsureSuccessStatusCode())).GetProperty("id").GetGuid();

        return new Shelf(bookId, algebra, geometry, await Question("Algebra question", algebra), await Question("Geometry question", geometry), await Question("Unfiled question", null));
    }

    private static async Task<(Guid ExamId, Guid SectionId)> NewExamAsync(HttpClient admin, object? scope = null)
    {
        var exam = await JsonAsync((await admin.PostAsJsonAsync("/v1/exams", new { name = "Scoped exam", scope })).EnsureSuccessStatusCode());
        var examId = exam.GetProperty("id").GetGuid();
        var section = await JsonAsync((await admin.PostAsJsonAsync($"/v1/exams/{examId}/sections", new { name = "Section A" })).EnsureSuccessStatusCode());
        return (examId, section.GetProperty("id").GetGuid());
    }

    private static Task<HttpResponseMessage> AddAsync(HttpClient admin, Guid examId, Guid sectionId, Guid questionId) =>
        admin.PostAsJsonAsync($"/v1/exams/{examId}/sections/{sectionId}/questions", new { questionId });

    private static Task<HttpResponseMessage> SetScopeAsync(HttpClient admin, Guid examId, object scope) =>
        admin.PutAsJsonAsync($"/v1/exams/{examId}/scope", scope);

    [Fact]
    public async Task AnExam_IsIndependentByDefault_AndTakesQuestionsFromAnywhere()
    {
        using var admin = await factory.AdminClientAsync();
        var shelf = await ShelfAsync(admin);
        var (examId, sectionId) = await NewExamAsync(admin);

        foreach (var question in new[] { shelf.InAlgebra, shelf.InGeometry, shelf.Unfiled })
            Assert.Equal(HttpStatusCode.Created, (await AddAsync(admin, examId, sectionId, question)).StatusCode);

        var exam = await admin.GetFromJsonAsync<JsonElement>($"/v1/exams/{examId}");
        Assert.Equal("Independent", exam.GetProperty("scope").GetProperty("type").GetString());
    }

    [Fact]
    public async Task AChapterWiseExam_TakesItsChapter_AndRefusesEverythingElse()
    {
        using var admin = await factory.AdminClientAsync();
        var shelf = await ShelfAsync(admin);
        var other = await ShelfAsync(admin, "Physics");
        var (examId, sectionId) = await NewExamAsync(admin, new { type = "Chapters", bookId = shelf.BookId, chapterIds = new[] { shelf.Algebra } });

        Assert.Equal(HttpStatusCode.Created, (await AddAsync(admin, examId, sectionId, shelf.InAlgebra)).StatusCode);

        foreach (var foreign in new[] { shelf.InGeometry, shelf.Unfiled, other.InAlgebra })
            await AssertProblemAsync(await AddAsync(admin, examId, sectionId, foreign), HttpStatusCode.Conflict, "question_outside_scope");

        var exam = await admin.GetFromJsonAsync<JsonElement>($"/v1/exams/{examId}");
        Assert.Single(exam.GetProperty("sections")[0].GetProperty("questions").EnumerateArray());
    }

    [Fact]
    public async Task AWholeBookExam_TakesEveryChapterOfTheBook_ButNotAnotherBook()
    {
        using var admin = await factory.AdminClientAsync();
        var shelf = await ShelfAsync(admin);
        var other = await ShelfAsync(admin, "Physics");
        var (examId, sectionId) = await NewExamAsync(admin, new { type = "Book", bookId = shelf.BookId });

        Assert.Equal(HttpStatusCode.Created, (await AddAsync(admin, examId, sectionId, shelf.InAlgebra)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await AddAsync(admin, examId, sectionId, shelf.InGeometry)).StatusCode);
        await AssertProblemAsync(await AddAsync(admin, examId, sectionId, other.InGeometry), HttpStatusCode.Conflict, "question_outside_scope");
        await AssertProblemAsync(await AddAsync(admin, examId, sectionId, shelf.Unfiled), HttpStatusCode.Conflict, "question_outside_scope");
    }

    [Fact]
    public async Task TheScope_IsReportedWithTheNamesOfTheBookAndChapters_InTheDetailAndInTheList()
    {
        using var admin = await factory.AdminClientAsync();
        var shelf = await ShelfAsync(admin, "Maths Grade 10");
        var (examId, _) = await NewExamAsync(admin, new { type = "Chapters", bookId = shelf.BookId, chapterIds = new[] { shelf.Geometry, shelf.Algebra } });

        var detail = (await admin.GetFromJsonAsync<JsonElement>($"/v1/exams/{examId}")).GetProperty("scope");
        Assert.Equal("Chapters", detail.GetProperty("type").GetString());
        Assert.StartsWith("Maths Grade 10", detail.GetProperty("bookName").GetString());
        Assert.Equal(["Geometry", "Algebra"], detail.GetProperty("chapters").EnumerateArray().Select(c => c.GetProperty("title").GetString()));

        var inList = (await admin.GetFromJsonAsync<JsonElement>("/v1/exams")).EnumerateArray().Single(e => e.GetProperty("id").GetGuid() == examId).GetProperty("scope");
        Assert.StartsWith("Maths Grade 10", inList.GetProperty("bookName").GetString());
        Assert.Equal(2, inList.GetProperty("chapters").GetArrayLength());
    }

    [Fact]
    public async Task ChangingTheScope_IsRefused_WhenItWouldLeaveAQuestionOutside_AndAllowedOtherwise()
    {
        using var admin = await factory.AdminClientAsync();
        var shelf = await ShelfAsync(admin);
        var (examId, sectionId) = await NewExamAsync(admin);
        await AddAsync(admin, examId, sectionId, shelf.InAlgebra);
        await AddAsync(admin, examId, sectionId, shelf.InGeometry);

        // Algebra only would leave the geometry question outside: refused, and nothing changed.
        await AssertProblemAsync(
            await SetScopeAsync(admin, examId, new { type = "Chapters", bookId = shelf.BookId, chapterIds = new[] { shelf.Algebra } }),
            HttpStatusCode.Conflict, "question_outside_scope");
        Assert.Equal("Independent", (await admin.GetFromJsonAsync<JsonElement>($"/v1/exams/{examId}")).GetProperty("scope").GetProperty("type").GetString());

        // The whole book holds both, so it is fine; and the limit can be lifted again.
        var whole = await JsonAsync(await SetScopeAsync(admin, examId, new { type = "Book", bookId = shelf.BookId }));
        Assert.Equal("Book", whole.GetProperty("scope").GetProperty("type").GetString());
        var lifted = await JsonAsync(await SetScopeAsync(admin, examId, new { type = "Independent" }));
        Assert.Equal("Independent", lifted.GetProperty("scope").GetProperty("type").GetString());
    }

    [Theory]
    [InlineData("missing book")]
    [InlineData("book without id")]
    [InlineData("chapters without any")]
    [InlineData("chapter of another book")]
    [InlineData("independent naming a book")]
    public async Task ABadScope_Returns400InvalidExamConfig_AndCreatesNothing(string scenario)
    {
        using var admin = await factory.AdminClientAsync();
        var shelf = await ShelfAsync(admin);
        var other = await ShelfAsync(admin, "Physics");
        object scope = scenario switch
        {
            "missing book" => new { type = "Book", bookId = Guid.NewGuid() },
            "book without id" => new { type = "Book" },
            "chapters without any" => new { type = "Chapters", bookId = shelf.BookId, chapterIds = Array.Empty<Guid>() },
            "chapter of another book" => new { type = "Chapters", bookId = shelf.BookId, chapterIds = new[] { other.Algebra } },
            _ => new { type = "Independent", bookId = shelf.BookId },
        };

        var before = (await admin.GetFromJsonAsync<JsonElement>("/v1/exams")).GetArrayLength();
        await AssertProblemAsync(await admin.PostAsJsonAsync("/v1/exams", new { name = "Bad scope", scope }), HttpStatusCode.BadRequest, "invalid_exam_config");
        Assert.Equal(before, (await admin.GetFromJsonAsync<JsonElement>("/v1/exams")).GetArrayLength());
    }

    [Fact]
    public async Task AnArchivedBookOrChapter_CannotBeChosenForANewScope()
    {
        using var admin = await factory.AdminClientAsync();
        var shelf = await ShelfAsync(admin);
        var (examId, _) = await NewExamAsync(admin);

        await admin.PostAsync($"/v1/books/{shelf.BookId}/chapters/{shelf.Geometry}/archive", null);
        await AssertProblemAsync(await SetScopeAsync(admin, examId, new { type = "Chapters", bookId = shelf.BookId, chapterIds = new[] { shelf.Geometry } }), HttpStatusCode.BadRequest, "invalid_exam_config");

        await admin.PostAsync($"/v1/books/{shelf.BookId}/archive", null);
        await AssertProblemAsync(await SetScopeAsync(admin, examId, new { type = "Book", bookId = shelf.BookId }), HttpStatusCode.BadRequest, "invalid_exam_config");
    }

    [Fact]
    public async Task ABadScopeType_Returns400NotACrash()
    {
        using var admin = await factory.AdminClientAsync();
        var (examId, _) = await NewExamAsync(admin);

        var response = await admin.PutAsJsonAsync($"/v1/exams/{examId}/scope", new { type = "Everything" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task APublishedExam_CannotChangeItsScope()
    {
        using var admin = await factory.AdminClientAsync();
        var shelf = await ShelfAsync(admin);
        var examId = await CreateExamAsync(admin, "Published", [shelf.InAlgebra], TimeSpan.FromMinutes(-5));

        await AssertProblemAsync(await SetScopeAsync(admin, examId, new { type = "Book", bookId = shelf.BookId }), HttpStatusCode.Conflict, "exam_not_draft");
    }

    [Fact]
    public async Task ASectionOfAScopedExam_StillPublishesAndIsTakenLikeAnyOther()
    {
        using var admin = await factory.AdminClientAsync();
        var shelf = await ShelfAsync(admin);
        var (examId, sectionId) = await NewExamAsync(admin, new { type = "Chapters", bookId = shelf.BookId, chapterIds = new[] { shelf.Algebra } });
        await AddAsync(admin, examId, sectionId, shelf.InAlgebra);
        var start = DateTime.UtcNow.AddMinutes(-5);
        (await admin.PutAsJsonAsync($"/v1/exams/{examId}/schedule", new { scheduledStartTime = start, scheduledEndTime = start.AddHours(3), durationMinutes = 30 })).EnsureSuccessStatusCode();
        var published = await JsonAsync((await admin.PostAsync($"/v1/exams/{examId}/publish", null)).EnsureSuccessStatusCode());

        Assert.Equal("Published", published.GetProperty("status").GetString());
        Assert.Equal("Chapters", published.GetProperty("scope").GetProperty("type").GetString());
        Assert.Equal(1, published.GetProperty("scope").GetProperty("chapters").GetArrayLength());
    }

    [Theory]
    [InlineData(RbacCatalog.RoleNames.Candidate)]
    [InlineData(RbacCatalog.RoleNames.Guardian)]
    public async Task ChangingAScope_NeedsTheExamAuthoringPermission(string role)
    {
        using var client = factory.CreateClient();
        var user = await factory.SignInAsAsync(role);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.AccessToken);

        var response = await client.PutAsJsonAsync($"/v1/exams/{Guid.NewGuid()}/scope", new { type = "Independent" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
