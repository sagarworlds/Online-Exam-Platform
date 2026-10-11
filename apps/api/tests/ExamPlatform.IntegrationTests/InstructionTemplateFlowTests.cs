using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// Instruction templates and an exam's instructions over real HTTP and a real database (FR-41). A template is written once and copied
/// into an exam; the copy is the exam's own, so editing the template later never changes it. An exam's instructions can change only while
/// it is a draft, because candidates acknowledge them before each attempt. Not run in this environment: the integration suite needs Docker.
/// </summary>
public sealed class InstructionTemplateFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await JsonAsync(response)).GetProperty("title").GetString());
    }

    private static string UniqueTitle() => $"Board rules {Guid.NewGuid():N}";

    private static async Task<Guid> CreateTemplateAsync(HttpClient admin, string title, string body)
    {
        var response = await admin.PostAsJsonAsync("/v1/instruction-templates", new { title, body });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await JsonAsync(response)).GetProperty("id").GetGuid();
    }

    private static async Task<string?> InstructionsOfAsync(HttpClient admin, Guid examId) =>
        (await JsonAsync((await admin.GetAsync($"/v1/exams/{examId}")).EnsureSuccessStatusCode())).GetProperty("instructions").GetString();

    private static async Task<Guid> CreateDraftExamAsync(HttpClient admin)
    {
        var created = await admin.PostAsJsonAsync("/v1/exams", new { name = "Instructions draft" });
        created.EnsureSuccessStatusCode();
        return (await JsonAsync(created)).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task CreateTemplate_ThenList_ShowsItByTitle()
    {
        using var admin = await factory.AdminClientAsync();
        var title = UniqueTitle();

        var id = await CreateTemplateAsync(admin, title, "Bring a pencil.");

        var listed = await JsonAsync((await admin.GetAsync("/v1/instruction-templates")).EnsureSuccessStatusCode());
        Assert.Contains(listed.EnumerateArray(), item => item.GetProperty("id").GetGuid() == id && item.GetProperty("title").GetString() == title);
    }

    [Fact]
    public async Task CreateTemplate_WithABlankBody_Returns400()
    {
        using var admin = await factory.AdminClientAsync();

        var response = await admin.PostAsJsonAsync("/v1/instruction-templates", new { title = UniqueTitle(), body = "   " });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "invalid_instruction_template");
    }

    [Fact]
    public async Task UseTemplate_CopiesItsText_IntoADraftExam()
    {
        using var admin = await factory.AdminClientAsync();
        var templateId = await CreateTemplateAsync(admin, UniqueTitle(), "Bring a pencil and an eraser.");
        var examId = await CreateDraftExamAsync(admin);

        (await admin.PostAsync($"/v1/exams/{examId}/instructions/from-template/{templateId}", content: null)).EnsureSuccessStatusCode();

        Assert.Equal("Bring a pencil and an eraser.", await InstructionsOfAsync(admin, examId));
    }

    [Fact]
    public async Task EditingATemplateLater_DoesNotChangeAnExamThatCopiedIt()
    {
        using var admin = await factory.AdminClientAsync();
        var title = UniqueTitle();
        var templateId = await CreateTemplateAsync(admin, title, "Bring a pencil.");
        var examId = await CreateDraftExamAsync(admin);
        (await admin.PostAsync($"/v1/exams/{examId}/instructions/from-template/{templateId}", content: null)).EnsureSuccessStatusCode();

        (await admin.PutAsJsonAsync($"/v1/instruction-templates/{templateId}", new { title, body = "Bring a calculator." })).EnsureSuccessStatusCode();

        Assert.Equal("Bring a pencil.", await InstructionsOfAsync(admin, examId));
    }

    [Fact]
    public async Task UseTemplate_OnAPublishedExam_Returns409()
    {
        using var admin = await factory.AdminClientAsync();
        var templateId = await CreateTemplateAsync(admin, UniqueTitle(), "Too late to change.");
        var questionId = await CreateQuestionAsync(admin, "Q?", "A", "B");
        var examId = await CreateExamAsync(admin, "Published exam", [questionId], TimeSpan.FromHours(1));

        var response = await admin.PostAsync($"/v1/exams/{examId}/instructions/from-template/{templateId}", content: null);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "exam_not_draft");
    }

    [Fact]
    public async Task SetInstructions_OnAPublishedExam_Returns409()
    {
        using var admin = await factory.AdminClientAsync();
        var questionId = await CreateQuestionAsync(admin, "Q?", "A", "B");
        var examId = await CreateExamAsync(admin, "Published exam", [questionId], TimeSpan.FromHours(1));

        var response = await admin.PutAsJsonAsync($"/v1/exams/{examId}/instructions", new { instructions = "Changed after publishing." });

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "exam_not_draft");
    }

    [Fact]
    public async Task InstructionsSetOnADraft_ReachTheCandidate_BeforeTheyStart()
    {
        using var admin = await factory.AdminClientAsync();
        var questionId = await CreateQuestionAsync(admin, "Q?", "A", "B");
        var examId = await CreateExamAsync(admin, "Candidate instructions", [questionId], TimeSpan.FromHours(-1), publish: false);
        const string text = "Read every question twice before you answer.";
        (await admin.PutAsJsonAsync($"/v1/exams/{examId}/instructions", new { instructions = text })).EnsureSuccessStatusCode();
        (await admin.PostAsync($"/v1/exams/{examId}/publish", content: null)).EnsureSuccessStatusCode();

        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);

        var mine = await JsonAsync((await candidate.GetAsync("/v1/me/exams")).EnsureSuccessStatusCode());
        var exam = mine.EnumerateArray().Single(item => item.GetProperty("examId").GetGuid() == examId);
        Assert.Equal(text, exam.GetProperty("rules").GetProperty("instructions").GetString());
    }

    [Fact]
    public async Task DeleteTemplate_RemovesIt_AndAnExamThatCopiedItKeepsItsText()
    {
        using var admin = await factory.AdminClientAsync();
        var templateId = await CreateTemplateAsync(admin, UniqueTitle(), "Keep your phone off.");
        var examId = await CreateDraftExamAsync(admin);
        (await admin.PostAsync($"/v1/exams/{examId}/instructions/from-template/{templateId}", content: null)).EnsureSuccessStatusCode();

        (await admin.DeleteAsync($"/v1/instruction-templates/{templateId}")).EnsureSuccessStatusCode();

        Assert.Equal("Keep your phone off.", await InstructionsOfAsync(admin, examId));
        await AssertProblemAsync(await admin.DeleteAsync($"/v1/instruction-templates/{templateId}"), HttpStatusCode.NotFound, "instruction_template_not_found");
    }

    [Fact]
    public async Task CandidateCannotCreateATemplate_Returns403()
    {
        var (candidate, _) = await factory.CandidateClientAsync();

        var response = await candidate.PostAsJsonAsync("/v1/instruction-templates", new { title = UniqueTitle(), body = "Nope." });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
