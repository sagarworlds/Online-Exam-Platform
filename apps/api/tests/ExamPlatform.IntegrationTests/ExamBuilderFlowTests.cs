using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ExamPlatform.Modules.Identity.Domain.Rbac;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// Drives the exam builder over real HTTP and a real database (FR-11, FR-13): an admin creates an exam,
/// adds sections and bank questions, schedules it and publishes it, and every refusal along the way is a typed error.
/// </summary>
public sealed class ExamBuilderFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateTime Start = DateTime.UtcNow.AddDays(2);
    private static readonly DateTime End = Start.AddHours(3);

    private async Task<HttpClient> AdminAsync(string role = RbacCatalog.RoleNames.ExamAdmin)
    {
        var client = factory.CreateClient();
        var user = await factory.SignInAsAsync(role);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.AccessToken);
        return client;
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<Guid> CreateQuestionAsync(HttpClient client, string text = "What is 2 + 2?")
    {
        var response = await client.PostAsJsonAsync("/v1/questions", new
        {
            text,
            options = new[] { new { text = "3", isCorrect = false }, new { text = "4", isCorrect = true } },
        });
        response.EnsureSuccessStatusCode();
        return (await ReadAsync(response)).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateExamAsync(HttpClient client, string name = "Builder Test Exam")
    {
        var response = await client.PostAsJsonAsync("/v1/exams", new { name });
        response.EnsureSuccessStatusCode();
        return (await ReadAsync(response)).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> AddSectionAsync(HttpClient client, Guid examId, string name = "Section A")
    {
        var response = await client.PostAsJsonAsync($"/v1/exams/{examId}/sections", new { name });
        response.EnsureSuccessStatusCode();
        return (await ReadAsync(response)).GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> ScheduleAsync(HttpClient client, Guid examId, object? body = null) =>
        client.PutAsJsonAsync($"/v1/exams/{examId}/schedule", body ?? new
        {
            scheduledStartTime = Start,
            scheduledEndTime = End,
            timeZone = "Asia/Kolkata",
            durationMinutes = 90,
        });

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string errorCode)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(errorCode, (await ReadAsync(response)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task TheWholeBuilderFlow_CreateAddSectionAddQuestionScheduleAndPublish_Works()
    {
        using var client = await AdminAsync();
        var questionId = await CreateQuestionAsync(client);
        var examId = await CreateExamAsync(client);

        var sectionId = await AddSectionAsync(client, examId, "  Algebra  ");
        var added = await client.PostAsJsonAsync($"/v1/exams/{examId}/sections/{sectionId}/questions", new { questionId });
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        Assert.Equal("What is 2 + 2?", (await ReadAsync(added)).GetProperty("text").GetString());

        var scheduled = await ScheduleAsync(client, examId);
        Assert.Equal(HttpStatusCode.OK, scheduled.StatusCode);
        var scheduledBody = await ReadAsync(scheduled);
        Assert.True(scheduledBody.GetProperty("isScheduled").GetBoolean());
        Assert.Equal(5400, scheduledBody.GetProperty("config").GetProperty("totalTimeSeconds").GetInt32());

        var published = await client.PostAsync($"/v1/exams/{examId}/publish", content: null);
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        Assert.Equal("Published", (await ReadAsync(published)).GetProperty("status").GetString());

        // Reading it back shows exactly what was built: persisted sections and questions, with the bank's text.
        var detail = await client.GetFromJsonAsync<JsonElement>($"/v1/exams/{examId}");
        Assert.Equal("Published", detail.GetProperty("status").GetString());
        var section = Assert.Single(detail.GetProperty("sections").EnumerateArray());
        Assert.Equal("Algebra", section.GetProperty("name").GetString());
        var question = Assert.Single(section.GetProperty("questions").EnumerateArray());
        Assert.Equal(questionId, question.GetProperty("questionId").GetGuid());
        Assert.Equal("What is 2 + 2?", question.GetProperty("text").GetString());
        Assert.Equal(Start, detail.GetProperty("scheduledStartTime").GetDateTime().ToUniversalTime(), TimeSpan.FromSeconds(1));
        Assert.Equal("Asia/Kolkata", detail.GetProperty("timeZone").GetString());

        // Scheduling replaces the exam's owned config; its nested marking scheme must come through intact.
        var marking = detail.GetProperty("config").GetProperty("markingScheme");
        Assert.Equal(1m, marking.GetProperty("correctMarks").GetDecimal());
        Assert.Equal(0m, marking.GetProperty("incorrectMarks").GetDecimal());
        Assert.Equal(0m, marking.GetProperty("unattemptedMarks").GetDecimal());
    }

    [Fact]
    public async Task Sections_AreNumberedInOrder_AndSurviveAReload()
    {
        using var client = await AdminAsync();
        var examId = await CreateExamAsync(client);

        await AddSectionAsync(client, examId, "One");
        await AddSectionAsync(client, examId, "Two");
        await AddSectionAsync(client, examId, "Three");

        var detail = await client.GetFromJsonAsync<JsonElement>($"/v1/exams/{examId}");
        var sections = detail.GetProperty("sections").EnumerateArray().ToList();
        Assert.Equal(["One", "Two", "Three"], sections.Select(s => s.GetProperty("name").GetString()));
        Assert.Equal([1, 2, 3], sections.Select(s => s.GetProperty("order").GetInt32()));
    }

    [Fact]
    public async Task List_IncludesTheExamWithoutItsSections_AndDetailIncludesThem()
    {
        using var client = await AdminAsync();
        var examId = await CreateExamAsync(client, "Listed Exam");
        await AddSectionAsync(client, examId);

        var list = await client.GetFromJsonAsync<JsonElement>("/v1/exams");

        var listed = Assert.Single(list.EnumerateArray(), e => e.GetProperty("id").GetGuid() == examId);
        Assert.Equal("Listed Exam", listed.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, listed.GetProperty("sections").ValueKind);
        Assert.False(listed.GetProperty("isScheduled").GetBoolean());
    }

    [Fact]
    public async Task UnknownExam_Returns404ExamNotFound_OnEveryRoute()
    {
        using var client = await AdminAsync();
        var unknown = Guid.NewGuid();

        await AssertProblemAsync(await client.GetAsync($"/v1/exams/{unknown}"), HttpStatusCode.NotFound, "exam_not_found");
        await AssertProblemAsync(await ScheduleAsync(client, unknown), HttpStatusCode.NotFound, "exam_not_found");
        await AssertProblemAsync(
            await client.PostAsJsonAsync($"/v1/exams/{unknown}/sections", new { name = "S" }), HttpStatusCode.NotFound, "exam_not_found");
        await AssertProblemAsync(await client.PostAsync($"/v1/exams/{unknown}/publish", null), HttpStatusCode.NotFound, "exam_not_found");
    }

    [Fact]
    public async Task AddQuestion_ThatIsNotInTheBank_Returns404()
    {
        using var client = await AdminAsync();
        var examId = await CreateExamAsync(client);
        var sectionId = await AddSectionAsync(client, examId);

        var response = await client.PostAsJsonAsync($"/v1/exams/{examId}/sections/{sectionId}/questions", new { questionId = Guid.NewGuid() });

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "question_not_found");
    }

    [Fact]
    public async Task AddQuestion_ToAnUnknownSection_Returns404()
    {
        using var client = await AdminAsync();
        var examId = await CreateExamAsync(client);
        var questionId = await CreateQuestionAsync(client);

        var response = await client.PostAsJsonAsync($"/v1/exams/{examId}/sections/{Guid.NewGuid()}/questions", new { questionId });

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "section_not_found");
    }

    [Fact]
    public async Task AddQuestion_TwiceToTheSameExam_Returns409Duplicate()
    {
        using var client = await AdminAsync();
        var examId = await CreateExamAsync(client);
        var sectionId = await AddSectionAsync(client, examId);
        var questionId = await CreateQuestionAsync(client);
        (await client.PostAsJsonAsync($"/v1/exams/{examId}/sections/{sectionId}/questions", new { questionId })).EnsureSuccessStatusCode();

        var again = await client.PostAsJsonAsync($"/v1/exams/{examId}/sections/{sectionId}/questions", new { questionId });

        await AssertProblemAsync(again, HttpStatusCode.Conflict, "duplicate_question");
    }

    [Fact]
    public async Task Schedule_WithBadInput_Returns400InvalidExamConfig()
    {
        using var client = await AdminAsync();
        var examId = await CreateExamAsync(client);

        await AssertProblemAsync(
            await ScheduleAsync(client, examId, new { scheduledStartTime = End, scheduledEndTime = Start }),
            HttpStatusCode.BadRequest, "invalid_exam_config");
        await AssertProblemAsync(
            await ScheduleAsync(client, examId, new { scheduledStartTime = DateTime.UtcNow.AddDays(-2), scheduledEndTime = DateTime.UtcNow.AddDays(-1) }),
            HttpStatusCode.BadRequest, "invalid_exam_config");
        await AssertProblemAsync(
            await ScheduleAsync(client, examId, new { scheduledStartTime = Start }),
            HttpStatusCode.BadRequest, "invalid_exam_config");
        await AssertProblemAsync(
            await ScheduleAsync(client, examId, new { scheduledStartTime = Start, scheduledEndTime = End, durationMinutes = 99999 }),
            HttpStatusCode.BadRequest, "invalid_exam_config");
    }

    [Fact]
    public async Task Publish_BeforeSchedulingOrWithoutQuestions_Returns400()
    {
        using var client = await AdminAsync();
        var examId = await CreateExamAsync(client);

        await AssertProblemAsync(await client.PostAsync($"/v1/exams/{examId}/publish", null), HttpStatusCode.BadRequest, "invalid_exam_config");

        (await ScheduleAsync(client, examId)).EnsureSuccessStatusCode();
        await AddSectionAsync(client, examId);
        var stillEmpty = await client.PostAsync($"/v1/exams/{examId}/publish", null);

        await AssertProblemAsync(stillEmpty, HttpStatusCode.BadRequest, "invalid_exam_config");
        Assert.Equal("Draft", (await client.GetFromJsonAsync<JsonElement>($"/v1/exams/{examId}")).GetProperty("status").GetString());
    }

    [Fact]
    public async Task APublishedExam_RefusesEdits_AndASecondPublish_With409()
    {
        using var client = await AdminAsync();
        var questionId = await CreateQuestionAsync(client);
        var examId = await CreateExamAsync(client);
        var sectionId = await AddSectionAsync(client, examId);
        (await client.PostAsJsonAsync($"/v1/exams/{examId}/sections/{sectionId}/questions", new { questionId })).EnsureSuccessStatusCode();
        (await ScheduleAsync(client, examId)).EnsureSuccessStatusCode();
        (await client.PostAsync($"/v1/exams/{examId}/publish", null)).EnsureSuccessStatusCode();

        await AssertProblemAsync(
            await client.PostAsJsonAsync($"/v1/exams/{examId}/sections", new { name = "Late" }), HttpStatusCode.Conflict, "exam_not_draft");
        await AssertProblemAsync(await ScheduleAsync(client, examId), HttpStatusCode.Conflict, "exam_not_draft");
        await AssertProblemAsync(await client.PostAsync($"/v1/exams/{examId}/publish", null), HttpStatusCode.Conflict, "exam_not_draft");
    }

    [Fact]
    public async Task ATeacher_CanReadExamsButNeitherChangeNorPublishThem()
    {
        // InstituteTeacher holds exam.read only: it must be refused on every route that changes an exam.
        using var teacher = await AdminAsync(RbacCatalog.RoleNames.InstituteTeacher);

        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.PostAsync($"/v1/exams/{Guid.NewGuid()}/publish", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.PostAsJsonAsync("/v1/exams", new { name = "Teacher Exam" })).StatusCode);

        // A teacher can read exams (to choose one to invite candidates to), but not change them.
        Assert.Equal(HttpStatusCode.OK, (await teacher.GetAsync("/v1/exams")).StatusCode);
    }
}
