using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// Putting right a draft exam (FR-11) over real HTTP and a real database: a question or section added by mistake can be taken
/// out again, the numbering stays whole, and a published exam, which candidates may already have sat, refuses every one of these.
/// </summary>
public sealed class ExamDraftCorrectionFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await JsonAsync(response)).GetProperty("title").GetString());
    }

    private static async Task<JsonElement> ReadExamAsync(HttpClient admin, Guid examId) =>
        await JsonAsync((await admin.GetAsync($"/v1/exams/{examId}")).EnsureSuccessStatusCode());

    private static async Task<Guid> FirstSectionAsync(HttpClient admin, Guid examId) =>
        (await ReadExamAsync(admin, examId)).GetProperty("sections")[0].GetProperty("id").GetGuid();

    private static IEnumerable<Guid> QuestionIdsIn(JsonElement exam) =>
        exam.GetProperty("sections").EnumerateArray()
            .SelectMany(section => section.GetProperty("questions").EnumerateArray())
            .Select(question => question.GetProperty("questionId").GetGuid());

    [Fact]
    public async Task ATakenOutQuestion_LeavesTheExam_TheOthersAreRenumbered_AndTheQuestionCanBeDeletedAgain()
    {
        using var admin = await factory.AdminClientAsync();
        var first = await CreateQuestionAsync(admin, "First", "A", "B");
        var second = await CreateQuestionAsync(admin, "Second", "A", "B");
        var third = await CreateQuestionAsync(admin, "Third", "A", "B");
        var examId = await CreateExamAsync(admin, "Draft with a mistake", [first, second, third], TimeSpan.FromHours(1), publish: false);
        var sectionId = await FirstSectionAsync(admin, examId);

        // While it is in the exam the bank refuses to delete it...
        await AssertProblemAsync(await admin.DeleteAsync($"/v1/questions/{second}"), HttpStatusCode.Conflict, "question_in_use");

        var removed = await admin.DeleteAsync($"/v1/exams/{examId}/sections/{sectionId}/questions/{second}");

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        var exam = await ReadExamAsync(admin, examId);
        Assert.Equal([first, third], QuestionIdsIn(exam));
        Assert.Equal([1, 2], exam.GetProperty("sections")[0].GetProperty("questions").EnumerateArray().Select(q => q.GetProperty("order").GetInt32()));

        // ...and once it is out, nothing holds it, so the bank lets it go.
        Assert.Equal(0, (await JsonAsync(await admin.GetAsync($"/v1/questions/{second}"))).GetProperty("usage").GetProperty("examCount").GetInt32());
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/v1/questions/{second}")).StatusCode);
    }

    [Fact]
    public async Task ATakenOutQuestion_CanBeAddedBackToTheExam()
    {
        using var admin = await factory.AdminClientAsync();
        var questionId = await CreateQuestionAsync(admin, "Out and back", "A", "B");
        var examId = await CreateExamAsync(admin, "Draft", [questionId], TimeSpan.FromHours(1), publish: false);
        var sectionId = await FirstSectionAsync(admin, examId);
        (await admin.DeleteAsync($"/v1/exams/{examId}/sections/{sectionId}/questions/{questionId}")).EnsureSuccessStatusCode();

        var added = await admin.PostAsJsonAsync($"/v1/exams/{examId}/sections/{sectionId}/questions", new { questionId });

        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        Assert.Equal([questionId], QuestionIdsIn(await ReadExamAsync(admin, examId)));
    }

    private static async Task<Guid> AddSectionAsync(HttpClient admin, Guid examId, string name)
    {
        var response = await admin.PostAsJsonAsync($"/v1/exams/{examId}/sections", new { name });
        response.EnsureSuccessStatusCode();
        return (await JsonAsync(response)).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task ATakenOutSection_LeavesTheExamWithItsQuestions_TheOthersAreRenumbered_AndItsQuestionsCanBeDeletedAgain()
    {
        using var admin = await factory.AdminClientAsync();
        var kept = await CreateQuestionAsync(admin, "Stays", "A", "B");
        var dropped = await CreateQuestionAsync(admin, "Goes with its section", "A", "B");
        var examId = await CreateExamAsync(admin, "Draft with a spare section", [kept], TimeSpan.FromHours(1), publish: false);
        var keptSection = await FirstSectionAsync(admin, examId);
        var spare = await AddSectionAsync(admin, examId, "Spare");
        var last = await AddSectionAsync(admin, examId, "Last");
        (await admin.PostAsJsonAsync($"/v1/exams/{examId}/sections/{spare}/questions", new { questionId = dropped })).EnsureSuccessStatusCode();
        await AssertProblemAsync(await admin.DeleteAsync($"/v1/questions/{dropped}"), HttpStatusCode.Conflict, "question_in_use");

        var removed = await admin.DeleteAsync($"/v1/exams/{examId}/sections/{spare}");

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        var exam = await ReadExamAsync(admin, examId);
        Assert.Equal([(keptSection, 1), (last, 2)], exam.GetProperty("sections").EnumerateArray().Select(s => (s.GetProperty("id").GetGuid(), s.GetProperty("order").GetInt32())));
        Assert.Equal([kept], QuestionIdsIn(exam));
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/v1/questions/{dropped}")).StatusCode);
    }

    [Fact]
    public async Task ARenamedSection_KeepsItsPlaceAndQuestions_AndTheNewTimeLimitIsShown()
    {
        using var admin = await factory.AdminClientAsync();
        var questionId = await CreateQuestionAsync(admin, "In the section", "A", "B");
        var examId = await CreateExamAsync(admin, "Draft", [questionId], TimeSpan.FromHours(1), publish: false);
        var sectionId = await FirstSectionAsync(admin, examId);

        var response = await admin.PutAsJsonAsync($"/v1/exams/{examId}/sections/{sectionId}", new { name = "  Algebra  ", timeSeconds = 1800 });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var section = (await ReadExamAsync(admin, examId)).GetProperty("sections")[0];
        Assert.Equal("Algebra", section.GetProperty("name").GetString());
        Assert.Equal(1800, section.GetProperty("timeSeconds").GetInt32());
        Assert.Equal(1, section.GetProperty("order").GetInt32());
        Assert.Equal(questionId, section.GetProperty("questions")[0].GetProperty("questionId").GetGuid());
    }

    [Fact]
    public async Task EditingASection_WithBadInput_Returns400_AndOnAPublishedOrUnknownOneIsRefused()
    {
        using var admin = await factory.AdminClientAsync();
        var draft = await CreateExamAsync(admin, "Draft", [await CreateQuestionAsync(admin, "Q1", "A", "B")], TimeSpan.FromHours(1), publish: false);
        var draftSection = await FirstSectionAsync(admin, draft);
        var published = await CreateExamAsync(admin, "Published", [await CreateQuestionAsync(admin, "Q2", "A", "B")], TimeSpan.FromMinutes(-5));
        var publishedSection = await FirstSectionAsync(admin, published);

        await AssertProblemAsync(await admin.PutAsJsonAsync($"/v1/exams/{draft}/sections/{draftSection}", new { name = "  " }), HttpStatusCode.BadRequest, "invalid_exam_config");
        await AssertProblemAsync(await admin.PutAsJsonAsync($"/v1/exams/{draft}/sections/{draftSection}", new { name = "Fine", timeSeconds = 0 }), HttpStatusCode.BadRequest, "invalid_exam_config");
        await AssertProblemAsync(await admin.PutAsJsonAsync($"/v1/exams/{draft}/sections/{Guid.NewGuid()}", new { name = "Fine" }), HttpStatusCode.NotFound, "section_not_found");
        await AssertProblemAsync(await admin.PutAsJsonAsync($"/v1/exams/{published}/sections/{publishedSection}", new { name = "Too late" }), HttpStatusCode.Conflict, "exam_not_draft");

        Assert.Equal("Section A", (await ReadExamAsync(admin, draft)).GetProperty("sections")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task TheNameAndDescription_CanBeChanged_InADraftAndInAPublishedExam()
    {
        using var admin = await factory.AdminClientAsync();
        var draft = await CreateExamAsync(admin, "Typo in the titel", [], TimeSpan.FromHours(1), publish: false);
        var published = await CreateExamAsync(admin, "Another titel", [await CreateQuestionAsync(admin, "Q", "A", "B")], TimeSpan.FromMinutes(-5));

        var changed = await admin.PutAsJsonAsync($"/v1/exams/{draft}/details", new { name = " Fixed title ", description = "Chapters 1 to 4" });
        var changedPublished = await admin.PutAsJsonAsync($"/v1/exams/{published}/details", new { name = "Another title", description = (string?)null });

        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var body = await JsonAsync(changed);
        Assert.Equal("Fixed title", body.GetProperty("name").GetString());
        Assert.Equal("Chapters 1 to 4", body.GetProperty("description").GetString());
        Assert.Equal("Fixed title", (await ReadExamAsync(admin, draft)).GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.OK, changedPublished.StatusCode);
        var stillPublished = await ReadExamAsync(admin, published);
        Assert.Equal("Another title", stillPublished.GetProperty("name").GetString());
        Assert.Equal("Published", stillPublished.GetProperty("status").GetString());
    }

    [Fact]
    public async Task ChangingTheDetails_WithABlankOrTooLongName_Returns400_AndOfAnUnknownExam_404()
    {
        using var admin = await factory.AdminClientAsync();
        var examId = await CreateExamAsync(admin, "Keep this name", [], TimeSpan.FromHours(1), publish: false);

        await AssertProblemAsync(await admin.PutAsJsonAsync($"/v1/exams/{examId}/details", new { name = "  " }), HttpStatusCode.BadRequest, "invalid_exam_config");
        await AssertProblemAsync(await admin.PutAsJsonAsync($"/v1/exams/{examId}/details", new { name = new string('x', 256) }), HttpStatusCode.BadRequest, "invalid_exam_config");
        await AssertProblemAsync(await admin.PutAsJsonAsync($"/v1/exams/{examId}/details", new { name = "Fine", description = new string('x', 1001) }), HttpStatusCode.BadRequest, "invalid_exam_config");
        await AssertProblemAsync(await admin.PutAsJsonAsync($"/v1/exams/{Guid.NewGuid()}/details", new { name = "Fine" }), HttpStatusCode.NotFound, "exam_not_found");

        Assert.Equal("Keep this name", (await ReadExamAsync(admin, examId)).GetProperty("name").GetString());
    }

    [Fact]
    public async Task TakingASectionOut_OfAPublishedExam_Returns409_AndChangesNothing()
    {
        using var admin = await factory.AdminClientAsync();
        var questionId = await CreateQuestionAsync(admin, "Published", "A", "B");
        var examId = await CreateExamAsync(admin, "Published exam", [questionId], TimeSpan.FromMinutes(-5));
        var sectionId = await FirstSectionAsync(admin, examId);

        await AssertProblemAsync(await admin.DeleteAsync($"/v1/exams/{examId}/sections/{sectionId}"), HttpStatusCode.Conflict, "exam_not_draft");

        Assert.Equal([questionId], QuestionIdsIn(await ReadExamAsync(admin, examId)));
    }

    [Fact]
    public async Task TakingASectionOut_ThatIsNotThere_Returns404()
    {
        using var admin = await factory.AdminClientAsync();
        var examId = await CreateExamAsync(admin, "Draft", [], TimeSpan.FromHours(1), publish: false);

        await AssertProblemAsync(await admin.DeleteAsync($"/v1/exams/{examId}/sections/{Guid.NewGuid()}"), HttpStatusCode.NotFound, "section_not_found");
        await AssertProblemAsync(await admin.DeleteAsync($"/v1/exams/{Guid.NewGuid()}/sections/{Guid.NewGuid()}"), HttpStatusCode.NotFound, "exam_not_found");
    }

    [Fact]
    public async Task TakingAQuestionOut_OfAPublishedExam_Returns409_AndChangesNothing()
    {
        using var admin = await factory.AdminClientAsync();
        var questionId = await CreateQuestionAsync(admin, "Published", "A", "B");
        var examId = await CreateExamAsync(admin, "Published exam", [questionId], TimeSpan.FromMinutes(-5));
        var sectionId = await FirstSectionAsync(admin, examId);

        await AssertProblemAsync(
            await admin.DeleteAsync($"/v1/exams/{examId}/sections/{sectionId}/questions/{questionId}"),
            HttpStatusCode.Conflict, "exam_not_draft");

        Assert.Equal([questionId], QuestionIdsIn(await ReadExamAsync(admin, examId)));
    }

    [Fact]
    public async Task TakingAQuestionOut_ThatIsNotThere_IsA404ThatSaysWhichThingWasMissing()
    {
        using var admin = await factory.AdminClientAsync();
        var inExam = await CreateQuestionAsync(admin, "In the exam", "A", "B");
        var elsewhere = await CreateQuestionAsync(admin, "Not in the exam", "A", "B");
        var examId = await CreateExamAsync(admin, "Draft", [inExam], TimeSpan.FromHours(1), publish: false);
        var sectionId = await FirstSectionAsync(admin, examId);

        await AssertProblemAsync(await admin.DeleteAsync($"/v1/exams/{examId}/sections/{sectionId}/questions/{elsewhere}"), HttpStatusCode.NotFound, "question_not_in_exam");
        await AssertProblemAsync(await admin.DeleteAsync($"/v1/exams/{examId}/sections/{Guid.NewGuid()}/questions/{inExam}"), HttpStatusCode.NotFound, "section_not_found");
        await AssertProblemAsync(await admin.DeleteAsync($"/v1/exams/{Guid.NewGuid()}/sections/{sectionId}/questions/{inExam}"), HttpStatusCode.NotFound, "exam_not_found");

        Assert.Equal([inExam], QuestionIdsIn(await ReadExamAsync(admin, examId)));
    }
}
