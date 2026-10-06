using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>The review and approval workflow of a question, with comments (FR-8).</summary>
public sealed class QuestionReviewFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<string> StatusOfAsync(HttpClient admin, Guid questionId) =>
        (await admin.GetFromJsonAsync<JsonElement>($"/v1/questions/{questionId}")).GetProperty("status").GetString()!;

    private static Task<HttpResponseMessage> StepAsync(HttpClient admin, Guid questionId, string step, string? comment = null) =>
        admin.PostAsJsonAsync($"/v1/questions/{questionId}/{step}", new { comment });

    private static async Task<JsonElement[]> LogAsync(HttpClient admin, Guid questionId) =>
        (await admin.GetFromJsonAsync<JsonElement>($"/v1/questions/{questionId}/review-log")).EnumerateArray().ToArray();

    private static async Task ResaveAsync(HttpClient admin, Guid questionId, string? newText = null)
    {
        var stored = await admin.GetFromJsonAsync<JsonElement>($"/v1/questions/{questionId}");
        var options = stored.GetProperty("options").EnumerateArray()
            .Select(o => new { id = o.GetProperty("id").GetGuid(), text = o.GetProperty("text").GetString(), isCorrect = o.GetProperty("isCorrect").GetBoolean() })
            .ToArray();
        (await admin.PutAsJsonAsync($"/v1/questions/{questionId}", new { text = newText ?? stored.GetProperty("text").GetString(), options })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task ANewQuestion_IsADraft_AndWalksThroughReviewToApproved_WithEachStepInItsThread()
    {
        using var admin = await factory.AdminClientAsync();
        var id = await CreateQuestionAsync(admin, "Workflow question?", "Yes", "No");
        Assert.Equal("draft", await StatusOfAsync(admin, id));

        var submitted = await JsonAsync((await StepAsync(admin, id, "submit-for-review", "Ready for you")).EnsureSuccessStatusCode());
        Assert.Equal("in_review", submitted.GetProperty("status").GetString());
        Assert.Equal("in_review", await StatusOfAsync(admin, id));

        var approved = await JsonAsync((await StepAsync(admin, id, "approve", "Looks right")).EnsureSuccessStatusCode());
        Assert.Equal("approved", approved.GetProperty("status").GetString());
        Assert.Equal("approved", await StatusOfAsync(admin, id));

        var log = await LogAsync(admin, id);
        Assert.Equal(["submitted", "approved"], log.Select(e => e.GetProperty("kind").GetString()));
        Assert.Equal(["Ready for you", "Looks right"], log.Select(e => e.GetProperty("comment").GetString()));
        Assert.All(log, e => Assert.Equal(1, e.GetProperty("versionNumber").GetInt32()));
        Assert.Equal(["in_review", "approved"], log.Select(e => e.GetProperty("statusAfter").GetString()));
        Assert.All(log, e => Assert.False(string.IsNullOrWhiteSpace(e.GetProperty("byLabel").GetString())));
    }

    [Fact]
    public async Task SendingAQuestionBack_NeedsAReason_AndReturnsItToDraft_WithTheReasonInTheThread()
    {
        using var admin = await factory.AdminClientAsync();
        var id = await CreateQuestionAsync(admin, "Needs work?", "A", "B");
        (await StepAsync(admin, id, "submit-for-review")).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.BadRequest, (await StepAsync(admin, id, "request-changes")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await StepAsync(admin, id, "request-changes", "   ")).StatusCode);
        Assert.Equal("in_review", await StatusOfAsync(admin, id));

        (await StepAsync(admin, id, "request-changes", "Option B is also correct")).EnsureSuccessStatusCode();

        Assert.Equal("draft", await StatusOfAsync(admin, id));
        var last = (await LogAsync(admin, id)).Last();
        Assert.Equal("changes_requested", last.GetProperty("kind").GetString());
        Assert.Equal("Option B is also correct", last.GetProperty("comment").GetString());
    }

    [Theory]
    [InlineData("approve")]
    [InlineData("request-changes")]
    public async Task OnlyAQuestionInReviewCanBeApprovedOrSentBack(string step)
    {
        using var admin = await factory.AdminClientAsync();
        var id = await CreateQuestionAsync(admin, "Still a draft?", "A", "B");

        var response = await StepAsync(admin, id, step, "x");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("invalid_question_status", (await JsonAsync(response)).GetProperty("title").GetString());
        Assert.Equal("draft", await StatusOfAsync(admin, id));
        Assert.Empty(await LogAsync(admin, id));
    }

    [Fact]
    public async Task ADraftCanOnlyBePutForwardOnce_AndAnApprovedQuestionCannotBePutForwardAgain()
    {
        using var admin = await factory.AdminClientAsync();
        var id = await CreateQuestionAsync(admin, "Twice?", "A", "B");
        (await StepAsync(admin, id, "submit-for-review")).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Conflict, (await StepAsync(admin, id, "submit-for-review")).StatusCode);

        (await StepAsync(admin, id, "approve")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await StepAsync(admin, id, "submit-for-review")).StatusCode);
    }

    [Fact]
    public async Task ChangingAnApprovedQuestion_SendsItBackToDraft_ButSavingItUnchangedDoesNot()
    {
        using var admin = await factory.AdminClientAsync();
        var id = await CreateQuestionAsync(admin, "Original?", "A", "B");
        (await StepAsync(admin, id, "submit-for-review")).EnsureSuccessStatusCode();
        (await StepAsync(admin, id, "approve")).EnsureSuccessStatusCode();

        await ResaveAsync(admin, id);
        Assert.Equal("approved", await StatusOfAsync(admin, id));

        await ResaveAsync(admin, id, "Changed after approval?");
        Assert.Equal("draft", await StatusOfAsync(admin, id));
    }

    [Fact]
    public async Task Commenting_WorksInAnyStatus_NeedsSomeText_AndAppearsInOrder()
    {
        using var admin = await factory.AdminClientAsync();
        var id = await CreateQuestionAsync(admin, "Talk about me?", "A", "B");

        (await admin.PostAsJsonAsync($"/v1/questions/{id}/comments", new { comment = "First thought" })).EnsureSuccessStatusCode();
        (await StepAsync(admin, id, "submit-for-review")).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/v1/questions/{id}/comments", new { comment = "Second thought" })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/v1/questions/{id}/comments", new { comment = "  " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/v1/questions/{id}/comments", new { comment = new string('x', 2001) })).StatusCode);
        var log = await LogAsync(admin, id);
        Assert.Equal(["commented", "submitted", "commented"], log.Select(e => e.GetProperty("kind").GetString()));
        Assert.Equal(["First thought", "", "Second thought"], log.Select(e => e.GetProperty("comment").GetString()));
        Assert.Equal("in_review", await StatusOfAsync(admin, id));
    }

    [Fact]
    public async Task AnUnknownQuestion_Is404_ForEveryStepAndForItsThread()
    {
        using var admin = await factory.AdminClientAsync();
        var unknown = Guid.NewGuid();

        foreach (var step in new[] { "submit-for-review", "approve", "request-changes", "retire", "restore", "comments" })
            Assert.Equal(HttpStatusCode.NotFound, (await StepAsync(admin, unknown, step, "x")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/v1/questions/{unknown}/review-log")).StatusCode);
    }

    [Fact]
    public async Task TheListCanBeNarrowedByStatus_AndAnUnknownStatusIsRefused()
    {
        using var admin = await factory.AdminClientAsync();
        var marker = $"Status filter {Guid.NewGuid():N}";
        var draft = await CreateQuestionAsync(admin, marker + " draft", "A", "B");
        var inReview = await CreateQuestionAsync(admin, marker + " review", "A", "B");
        (await StepAsync(admin, inReview, "submit-for-review")).EnsureSuccessStatusCode();

        var review = await admin.GetFromJsonAsync<JsonElement>($"/v1/questions?q={Uri.EscapeDataString(marker)}&status=in_review");
        var drafts = await admin.GetFromJsonAsync<JsonElement>($"/v1/questions?q={Uri.EscapeDataString(marker)}&status=draft");

        Assert.Equal([inReview], review.EnumerateArray().Select(q => q.GetProperty("id").GetGuid()));
        Assert.Equal([draft], drafts.EnumerateArray().Select(q => q.GetProperty("id").GetGuid()));
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/v1/questions?status=nope")).StatusCode);
    }

    [Fact]
    public async Task ARetiredQuestion_CannotBeAddedToAnExam_UntilItIsRestored()
    {
        using var admin = await factory.AdminClientAsync();
        var keep = await CreateQuestionAsync(admin, "Already in the exam?", "A", "B");
        var retiring = await CreateQuestionAsync(admin, "Retiring soon?", "A", "B");
        var examId = await CreateExamAsync(admin, "Retire gate", [keep], TimeSpan.FromMinutes(-5), publish: false);
        var sectionId = (await admin.GetFromJsonAsync<JsonElement>($"/v1/exams/{examId}")).GetProperty("sections")[0].GetProperty("id").GetGuid();
        (await StepAsync(admin, retiring, "retire", "Out of date")).EnsureSuccessStatusCode();

        var refused = await admin.PostAsJsonAsync($"/v1/exams/{examId}/sections/{sectionId}/questions", new { questionId = retiring });

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("question_not_usable", (await JsonAsync(refused)).GetProperty("title").GetString());

        (await StepAsync(admin, retiring, "restore")).EnsureSuccessStatusCode();
        Assert.Equal("draft", await StatusOfAsync(admin, retiring));
        (await admin.PostAsJsonAsync($"/v1/exams/{examId}/sections/{sectionId}/questions", new { questionId = retiring })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task AQuestionRetiredAfterAnExamHoldsIt_IsStillSatByCandidates()
    {
        using var admin = await factory.AdminClientAsync();
        var question = await CreateQuestionAsync(admin, "Retired but in use?", "A", "B");
        var examId = await CreateExamAsync(admin, "Retired in use", [question], TimeSpan.FromMinutes(-5));
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;
        (await StepAsync(admin, question, "retire")).EnsureSuccessStatusCode();

        var started = await JsonAsync((await candidate.PostAsJsonAsync($"/v1/me/exams/{examId}/attempts", new { instructionsAcknowledged = true })).EnsureSuccessStatusCode());

        Assert.Contains("Retired but in use?", started.GetProperty("sections")[0].GetProperty("questions")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task ARetiredQuestion_CanBeRetiredOnlyOnce_AndOnlyARetiredOneRestored()
    {
        using var admin = await factory.AdminClientAsync();
        var id = await CreateQuestionAsync(admin, "Retire me?", "A", "B");

        Assert.Equal(HttpStatusCode.Conflict, (await StepAsync(admin, id, "restore")).StatusCode);
        (await StepAsync(admin, id, "retire")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await StepAsync(admin, id, "retire")).StatusCode);
        Assert.Equal("retired", await StatusOfAsync(admin, id));
    }

    [Fact]
    public async Task WhereApprovalIsRequired_OnlyAnApprovedQuestionCanBeAddedToAnExam()
    {
        await using var strict = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["QuestionBank:RequireApproval"] = "true" })));
        // The base factory signs the administrator in (its database is the same one); the client talks to the host that requires approval.
        using var admin = strict.CreateClient();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await factory.SignInAsAsync("SuperAdmin")).AccessToken);
        var approved = await CreateQuestionAsync(admin, "Approved one?", "A", "B");
        (await StepAsync(admin, approved, "submit-for-review")).EnsureSuccessStatusCode();
        (await StepAsync(admin, approved, "approve")).EnsureSuccessStatusCode();
        var draft = await CreateQuestionAsync(admin, "Draft one?", "A", "B");
        var examId = await CreateExamAsync(admin, "Strict", [approved], TimeSpan.FromMinutes(-5), publish: false);
        var sectionId = (await admin.GetFromJsonAsync<JsonElement>($"/v1/exams/{examId}")).GetProperty("sections")[0].GetProperty("id").GetGuid();

        var refused = await admin.PostAsJsonAsync($"/v1/exams/{examId}/sections/{sectionId}/questions", new { questionId = draft });

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        var problem = await JsonAsync(refused);
        Assert.Equal("question_not_usable", problem.GetProperty("title").GetString());
        Assert.Contains("approved", problem.GetProperty("detail").GetString());
    }
}
