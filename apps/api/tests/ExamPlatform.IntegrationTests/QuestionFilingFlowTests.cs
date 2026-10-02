using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExamPlatform.Modules.QuestionBank.Contracts;
using Microsoft.Extensions.DependencyInjection;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// Filing questions that are already in the bank under a book and chapter (FR-5), singly and in bulk, over real HTTP and a real
/// database: the move is all or nothing, and it will not pull a question out from under a draft exam's scope.
/// </summary>
public sealed class QuestionFilingFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record Shelf(Guid BookId, Guid Algebra, Guid Geometry);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<JsonElement> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var problem = await JsonAsync(response);
        Assert.Equal(code, problem.GetProperty("title").GetString());
        return problem;
    }

    private static async Task<Shelf> ShelfAsync(HttpClient admin)
    {
        var book = await JsonAsync((await admin.PostAsJsonAsync("/v1/books", new { name = $"Maths {Guid.NewGuid():N}" })).EnsureSuccessStatusCode());
        var bookId = book.GetProperty("id").GetGuid();
        await admin.PostAsJsonAsync($"/v1/books/{bookId}/chapters", new { title = "Algebra" });
        var withChapters = await JsonAsync((await admin.PostAsJsonAsync($"/v1/books/{bookId}/chapters", new { title = "Geometry" })).EnsureSuccessStatusCode());
        Guid Chapter(string title) => withChapters.GetProperty("chapters").EnumerateArray().Single(c => c.GetProperty("title").GetString() == title).GetProperty("id").GetGuid();
        return new Shelf(bookId, Chapter("Algebra"), Chapter("Geometry"));
    }

    private static Task<HttpResponseMessage> FileAsync(HttpClient admin, Guid chapterId, params Guid[] questionIds) =>
        admin.PostAsJsonAsync("/v1/questions/placement", new { questionIds, chapterId });

    private static async Task<Guid?> ChapterOfAsync(HttpClient admin, Guid questionId)
    {
        var chapter = (await admin.GetFromJsonAsync<JsonElement>($"/v1/questions/{questionId}")).GetProperty("chapterId");
        return chapter.ValueKind == JsonValueKind.Null ? null : chapter.GetGuid();
    }

    private static async Task<int> QuestionCountAsync(HttpClient admin, Guid bookId, Guid chapterId)
    {
        var book = await admin.GetFromJsonAsync<JsonElement>($"/v1/books/{bookId}");
        return book.GetProperty("chapters").EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == chapterId).GetProperty("questionCount").GetInt32();
    }

    [Fact]
    public async Task UnfiledQuestions_AreFiledInBulk_AndTheChapterCountsThem()
    {
        using var admin = await factory.AdminClientAsync();
        var shelf = await ShelfAsync(admin);
        var questions = new List<Guid>();
        foreach (var text in new[] { "One", "Two", "Three" })
            questions.Add(await CreateQuestionAsync(admin, text, "A", "B"));

        var result = await JsonAsync((await FileAsync(admin, shelf.Algebra, [.. questions])).EnsureSuccessStatusCode());

        Assert.Equal(3, result.GetProperty("moved").GetInt32());
        Assert.Equal("Algebra", result.GetProperty("chapterTitle").GetString());
        Assert.Equal(shelf.BookId, result.GetProperty("bookId").GetGuid());
        foreach (var question in questions)
            Assert.Equal(shelf.Algebra, await ChapterOfAsync(admin, question));
        Assert.Equal(3, await QuestionCountAsync(admin, shelf.BookId, shelf.Algebra));

        // And the question list under that chapter finds them: the filter and the filing agree.
        var listed = (await admin.GetFromJsonAsync<JsonElement>($"/v1/questions?chapterId={shelf.Algebra}")).EnumerateArray().Select(q => q.GetProperty("id").GetGuid());
        Assert.Equal(questions.Order(), listed.Order());
    }

    [Fact]
    public async Task OneQuestion_CanBeMovedFromOneChapterToAnother_AndFilingAgainIsHarmless()
    {
        using var admin = await factory.AdminClientAsync();
        var shelf = await ShelfAsync(admin);
        var question = await CreateQuestionAsync(admin, "Movable", "A", "B");
        await FileAsync(admin, shelf.Algebra, question);

        var moved = await JsonAsync((await FileAsync(admin, shelf.Geometry, question)).EnsureSuccessStatusCode());
        var again = await JsonAsync((await FileAsync(admin, shelf.Geometry, question)).EnsureSuccessStatusCode());

        Assert.Equal(1, moved.GetProperty("moved").GetInt32());
        Assert.Equal(0, again.GetProperty("moved").GetInt32());
        Assert.Equal(shelf.Geometry, await ChapterOfAsync(admin, question));
        Assert.Equal(0, await QuestionCountAsync(admin, shelf.BookId, shelf.Algebra));
        Assert.Equal(1, await QuestionCountAsync(admin, shelf.BookId, shelf.Geometry));
    }

    [Fact]
    public async Task FilingDoesNotChangeTheQuestionsContent_OrItsUsage()
    {
        using var admin = await factory.AdminClientAsync();
        var shelf = await ShelfAsync(admin);
        var question = await CreateQuestionAsync(admin, "Keep me", "Right", "Wrong");
        await CreateExamAsync(admin, "Independent exam", [question], TimeSpan.FromMinutes(-5));
        var before = await admin.GetFromJsonAsync<JsonElement>($"/v1/questions/{question}");

        (await FileAsync(admin, shelf.Algebra, question)).EnsureSuccessStatusCode();

        var after = await admin.GetFromJsonAsync<JsonElement>($"/v1/questions/{question}");
        Assert.Equal(before.GetProperty("text").ToString(), after.GetProperty("text").ToString());
        Assert.Equal(before.GetProperty("options").ToString(), after.GetProperty("options").ToString());
        Assert.Equal(1, after.GetProperty("usage").GetProperty("examCount").GetInt32());
    }

    [Fact]
    public async Task AnArchivedChapter_TakesNothingNew()
    {
        using var admin = await factory.AdminClientAsync();
        var shelf = await ShelfAsync(admin);
        var question = await CreateQuestionAsync(admin, "Q", "A", "B");
        (await admin.PostAsync($"/v1/books/{shelf.BookId}/chapters/{shelf.Algebra}/archive", content: null)).EnsureSuccessStatusCode();

        await AssertProblemAsync(await FileAsync(admin, shelf.Algebra, question), HttpStatusCode.Conflict, "book_archived");

        Assert.Null(await ChapterOfAsync(admin, question));
    }

    [Fact]
    public async Task AnUnknownChapter_Is404_AndAnUnknownQuestionRefusesTheWholeRequest()
    {
        using var admin = await factory.AdminClientAsync();
        var shelf = await ShelfAsync(admin);
        var real = await CreateQuestionAsync(admin, "Real", "A", "B");

        await AssertProblemAsync(await FileAsync(admin, Guid.NewGuid(), real), HttpStatusCode.NotFound, "chapter_not_found");
        await AssertProblemAsync(await FileAsync(admin, shelf.Algebra, real, Guid.NewGuid()), HttpStatusCode.NotFound, "question_not_found");

        Assert.Null(await ChapterOfAsync(admin, real)); // all or nothing
    }

    [Fact]
    public async Task NoQuestionsChosen_Is400()
    {
        using var admin = await factory.AdminClientAsync();
        var shelf = await ShelfAsync(admin);

        await AssertProblemAsync(await FileAsync(admin, shelf.Algebra), HttpStatusCode.BadRequest, "invalid_question");
    }

    // ---- exams that are limited to a book or chapters ---------------------------------------------

    private static async Task<Guid> DraftExamWithScopeAsync(HttpClient admin, object scope, Guid questionId)
    {
        var exam = await JsonAsync((await admin.PostAsJsonAsync("/v1/exams", new { name = "Scoped draft", scope })).EnsureSuccessStatusCode());
        var examId = exam.GetProperty("id").GetGuid();
        var section = await JsonAsync((await admin.PostAsJsonAsync($"/v1/exams/{examId}/sections", new { name = "Section A" })).EnsureSuccessStatusCode());
        (await admin.PostAsJsonAsync($"/v1/exams/{examId}/sections/{section.GetProperty("id").GetGuid()}/questions", new { questionId })).EnsureSuccessStatusCode();
        return examId;
    }

    [Fact]
    public async Task AQuestionInAChapterWiseDraft_CannotBeMovedOutOfThoseChapters_AndNothingMoves()
    {
        using var admin = await factory.AdminClientAsync();
        var shelf = await ShelfAsync(admin);
        var held = await CreateQuestionAsync(admin, "Held", "A", "B");
        var free = await CreateQuestionAsync(admin, "Free", "A", "B");
        await FileAsync(admin, shelf.Algebra, held, free);
        await DraftExamWithScopeAsync(admin, new { type = "Chapters", bookId = shelf.BookId, chapterIds = new[] { shelf.Algebra } }, held);

        var problem = await AssertProblemAsync(await FileAsync(admin, shelf.Geometry, held, free), HttpStatusCode.Conflict, "placement_refused");

        Assert.Contains("Scoped draft", problem.GetProperty("detail").GetString());
        Assert.Contains("Nothing was moved", problem.GetProperty("detail").GetString());
        Assert.Equal(shelf.Algebra, await ChapterOfAsync(admin, held));
        Assert.Equal(shelf.Algebra, await ChapterOfAsync(admin, free)); // the question that was fine did not move either
    }

    [Fact]
    public async Task AQuestionInAWholeBookDraft_MayMoveToAnotherChapterOfThatBook()
    {
        using var admin = await factory.AdminClientAsync();
        var shelf = await ShelfAsync(admin);
        var held = await CreateQuestionAsync(admin, "Held", "A", "B");
        await FileAsync(admin, shelf.Algebra, held);
        await DraftExamWithScopeAsync(admin, new { type = "Book", bookId = shelf.BookId }, held);

        (await FileAsync(admin, shelf.Geometry, held)).EnsureSuccessStatusCode();

        Assert.Equal(shelf.Geometry, await ChapterOfAsync(admin, held));
    }

    [Fact]
    public async Task AQuestionInAnIndependentExam_MayMoveAnywhere()
    {
        using var admin = await factory.AdminClientAsync();
        var shelf = await ShelfAsync(admin);
        var held = await CreateQuestionAsync(admin, "Held", "A", "B");
        await CreateExamAsync(admin, "Independent draft", [held], TimeSpan.FromHours(1), publish: false);

        (await FileAsync(admin, shelf.Algebra, held)).EnsureSuccessStatusCode();

        Assert.Equal(shelf.Algebra, await ChapterOfAsync(admin, held));
    }

    [Fact]
    public void EveryModuleThatDependsOnPlacement_HasRegisteredAGuard_SoNoMoveIsAllowedByOmission()
    {
        using var scope = factory.Services.CreateScope();

        var guards = scope.ServiceProvider.GetServices<IQuestionPlacementGuard>().Select(g => g.GetType().Name);

        Assert.Equal(["ExamScopePlacementGuard"], guards);
    }
}
