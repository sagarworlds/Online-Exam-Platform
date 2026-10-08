using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExamPlatform.Modules.Admin.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// Reporting a problem from inside an exam (FR-42) over real HTTP and a real database: a candidate who is sitting an exam says a question
/// or the page is wrong without leaving it, staff read the report in a queue and resolve it, and both steps are in the audit trail.
/// </summary>
public sealed class IssueReportFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await JsonAsync(response)).GetProperty("title").GetString());
    }

    /// <summary>One question in a published exam with a candidate invited, whose attempt is open.</summary>
    private sealed record Sitting(HttpClient Admin, HttpClient Candidate, Guid ExamId, Guid QuestionId, Guid AttemptId, string ExamName);

    private async Task<Sitting> SittingAsync()
    {
        var admin = await factory.AdminClientAsync();
        var questionId = await CreateQuestionAsync(admin, "Capital of France?", "Paris", "Rome");
        var name = $"Geography {Guid.NewGuid():N}";
        var examId = await CreateExamAsync(admin, name, [questionId], TimeSpan.FromMinutes(-5));
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        var started = await candidate.PostAsJsonAsync($"/v1/me/exams/{examId}/attempts", new { instructionsAcknowledged = true });
        var attemptId = (await JsonAsync(started.EnsureSuccessStatusCode())).GetProperty("id").GetGuid();
        return new Sitting(admin, candidate, examId, questionId, attemptId, name);
    }

    private static Task<HttpResponseMessage> ReportAsync(
        HttpClient candidate, Guid attemptId, string? category = "Question", string? message = "Option C is missing", Guid? questionId = null) =>
        candidate.PostAsJsonAsync($"/v1/me/attempts/{attemptId}/issues", new { category, message, questionId });

    private static async Task<JsonElement?> QueueEntryAsync(HttpClient admin, Guid reportId, string? status = null)
    {
        var listed = await JsonAsync((await admin.GetAsync(status is null ? "/v1/issue-reports" : $"/v1/issue-reports?status={status}")).EnsureSuccessStatusCode());
        foreach (var entry in listed.EnumerateArray())
            if (entry.GetProperty("id").GetGuid() == reportId)
                return entry;
        return null;
    }

    [Fact]
    public async Task AProblemReportedFromTheExam_IsQueuedForStaff_AndResolvedWithANote()
    {
        var s = await SittingAsync();
        using var _a = s.Admin;
        using var _c = s.Candidate;

        var created = await ReportAsync(s.Candidate, s.AttemptId, "question", "  Option C is missing  ", s.QuestionId);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var reported = await JsonAsync(created);
        var reportId = reported.GetProperty("id").GetGuid();
        Assert.Equal("Question", reported.GetProperty("category").GetString());
        Assert.Equal("Option C is missing", reported.GetProperty("message").GetString());

        // Staff see it, with who, which exam, which attempt and which question.
        var queued = (await QueueEntryAsync(s.Admin, reportId))!.Value;
        Assert.Equal(s.ExamName, queued.GetProperty("examName").GetString());
        Assert.Equal(1, queued.GetProperty("attemptNumber").GetInt32());
        Assert.Equal(s.QuestionId, queued.GetProperty("questionId").GetGuid());
        Assert.Contains("Capital of France?", queued.GetProperty("questionText").GetString());
        Assert.Equal("Option C is missing", queued.GetProperty("message").GetString());
        Assert.Contains("@tests.local", queued.GetProperty("candidateEmail").GetString());
        Assert.Equal("Open", queued.GetProperty("status").GetString());

        // The attempt is untouched: the candidate is still sitting it and can still answer.
        var attempt = await s.Candidate.GetFromJsonAsync<JsonElement>($"/v1/me/attempts/{s.AttemptId}");
        Assert.Equal("InProgress", attempt.GetProperty("status").GetString());

        var resolved = await s.Admin.PostAsJsonAsync($"/v1/issue-reports/{reportId}/resolve", new { note = "  Added the option  " });

        Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);
        var settled = await JsonAsync(resolved);
        Assert.Equal("Resolved", settled.GetProperty("status").GetString());
        Assert.Equal("Added the option", settled.GetProperty("resolutionNote").GetString());

        // It leaves the open queue and is found among the resolved ones.
        Assert.Null(await QueueEntryAsync(s.Admin, reportId));
        Assert.NotNull(await QueueEntryAsync(s.Admin, reportId, "resolved"));
    }

    [Fact]
    public async Task AReportAboutThePage_NeedsNoQuestion_AndAResolutionNoNote()
    {
        var s = await SittingAsync();
        using var _a = s.Admin;
        using var _c = s.Candidate;

        var reportId = (await JsonAsync((await ReportAsync(s.Candidate, s.AttemptId, "Technical", "The timer froze")).EnsureSuccessStatusCode()))
            .GetProperty("id").GetGuid();

        var queued = (await QueueEntryAsync(s.Admin, reportId))!.Value;
        Assert.Equal(JsonValueKind.Null, queued.GetProperty("questionId").ValueKind);
        Assert.Equal("Technical", queued.GetProperty("category").GetString());

        var resolved = await s.Admin.PostAsJsonAsync($"/v1/issue-reports/{reportId}/resolve", new { });

        Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await JsonAsync(resolved)).GetProperty("resolutionNote").ValueKind);
    }

    [Fact]
    public async Task ABadRequest_IsRefusedWithAReason_AndNothingIsQueued()
    {
        var s = await SittingAsync();
        using var _a = s.Admin;
        using var _c = s.Candidate;

        await AssertProblemAsync(await ReportAsync(s.Candidate, s.AttemptId, "Question", "   "), HttpStatusCode.BadRequest, "invalid_attempt");
        await AssertProblemAsync(await ReportAsync(s.Candidate, s.AttemptId, "Spam", "Hello"), HttpStatusCode.BadRequest, "invalid_attempt");
        await AssertProblemAsync(await ReportAsync(s.Candidate, s.AttemptId, null, "Hello"), HttpStatusCode.BadRequest, "invalid_attempt");
        await AssertProblemAsync(await ReportAsync(s.Candidate, s.AttemptId, "Other", new string('x', 1001)), HttpStatusCode.BadRequest, "invalid_attempt");
        await AssertProblemAsync(
            await ReportAsync(s.Candidate, s.AttemptId, "Question", "Hello", Guid.NewGuid()), HttpStatusCode.NotFound, "question_not_in_attempt");

        var listed = await JsonAsync((await s.Admin.GetAsync("/v1/issue-reports")).EnsureSuccessStatusCode());
        Assert.DoesNotContain(listed.EnumerateArray(), e => e.GetProperty("attemptId").GetGuid() == s.AttemptId);
    }

    [Fact]
    public async Task ANoteThatIsTooLong_IsRefused_AndTheReportStaysOpen()
    {
        var s = await SittingAsync();
        using var _a = s.Admin;
        using var _c = s.Candidate;
        var reportId = (await JsonAsync((await ReportAsync(s.Candidate, s.AttemptId)).EnsureSuccessStatusCode())).GetProperty("id").GetGuid();

        await AssertProblemAsync(
            await s.Admin.PostAsJsonAsync($"/v1/issue-reports/{reportId}/resolve", new { note = new string('x', 501) }),
            HttpStatusCode.BadRequest,
            "invalid_attempt");

        Assert.NotNull(await QueueEntryAsync(s.Admin, reportId));
    }

    [Fact]
    public async Task OnlyTheOwner_CanReportFromAnAttempt()
    {
        var s = await SittingAsync();
        using var _a = s.Admin;
        using var _c = s.Candidate;
        var (other, _) = await factory.EnrollNewCandidateAsync(s.Admin, s.ExamId);
        using var _o = other;

        await AssertProblemAsync(await ReportAsync(other, s.AttemptId), HttpStatusCode.NotFound, "attempt_not_found");

        using var anonymous = factory.CreateClient();
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.PostAsJsonAsync($"/v1/me/attempts/{s.AttemptId}/issues", new { category = "Other", message = "Hello" })).StatusCode);
    }

    [Fact]
    public async Task AnAttemptThatIsOver_CanNoLongerReport()
    {
        var s = await SittingAsync();
        using var _a = s.Admin;
        using var _c = s.Candidate;
        (await s.Candidate.PostAsync($"/v1/me/attempts/{s.AttemptId}/submit", content: null)).EnsureSuccessStatusCode();

        await AssertProblemAsync(await ReportAsync(s.Candidate, s.AttemptId), HttpStatusCode.Conflict, "attempt_not_in_progress");
    }

    [Fact]
    public async Task AnAttempt_CarriesOnlyTenReports()
    {
        var s = await SittingAsync();
        using var _a = s.Admin;
        using var _c = s.Candidate;

        for (var i = 1; i <= 10; i++)
            (await ReportAsync(s.Candidate, s.AttemptId, "Other", $"Report {i}")).EnsureSuccessStatusCode();

        await AssertProblemAsync(await ReportAsync(s.Candidate, s.AttemptId, "Other", "One too many"), (HttpStatusCode)429, "too_many_issue_reports");
    }

    [Fact]
    public async Task AResolvedReport_CannotBeResolvedAgain_AndAnUnknownOneIsNotFound()
    {
        var s = await SittingAsync();
        using var _a = s.Admin;
        using var _c = s.Candidate;
        var reportId = (await JsonAsync((await ReportAsync(s.Candidate, s.AttemptId)).EnsureSuccessStatusCode())).GetProperty("id").GetGuid();
        (await s.Admin.PostAsJsonAsync($"/v1/issue-reports/{reportId}/resolve", new { note = "Done" })).EnsureSuccessStatusCode();

        await AssertProblemAsync(
            await s.Admin.PostAsJsonAsync($"/v1/issue-reports/{reportId}/resolve", new { note = "Again" }), HttpStatusCode.Conflict, "issue_report_not_open");
        await AssertProblemAsync(
            await s.Admin.PostAsJsonAsync($"/v1/issue-reports/{Guid.NewGuid()}/resolve", new { }), HttpStatusCode.NotFound, "issue_report_not_found");
        await AssertProblemAsync(await s.Admin.GetAsync("/v1/issue-reports?status=maybe"), HttpStatusCode.BadRequest, "invalid_attempt");
    }

    [Fact]
    public async Task TheStaffQueue_IsForStaff_NotForCandidates()
    {
        var s = await SittingAsync();
        using var _a = s.Admin;
        using var _c = s.Candidate;

        Assert.Equal(HttpStatusCode.Forbidden, (await s.Candidate.GetAsync("/v1/issue-reports")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await s.Candidate.PostAsJsonAsync($"/v1/issue-reports/{Guid.NewGuid()}/resolve", new { note = "x" })).StatusCode);
    }

    [Fact]
    public async Task ReportingAndResolving_AreAudited_WithoutWhatTheCandidateWrote()
    {
        var s = await SittingAsync();
        using var _a = s.Admin;
        using var _c = s.Candidate;
        var reportId = (await JsonAsync((await ReportAsync(s.Candidate, s.AttemptId, "Technical", "My private words")).EnsureSuccessStatusCode()))
            .GetProperty("id").GetGuid();
        (await s.Admin.PostAsJsonAsync($"/v1/issue-reports/{reportId}/resolve", new { note = "Looked into it" })).EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var entries = await scope.ServiceProvider.GetRequiredService<AdminDbContext>().AuditLogs
            .AsNoTracking()
            .Where(a => a.EntityId == s.AttemptId.ToString())
            .OrderBy(a => a.OccurredAtUtc)
            .ToListAsync();

        Assert.Equal(["ExamRuntime.IssueReported", "ExamRuntime.IssueResolved"], entries.Select(e => e.Action).ToArray());
        Assert.NotNull(entries[0].ActorUserId);
        Assert.NotNull(entries[1].ActorUserId);
        Assert.Equal(reportId.ToString(), entries[0].Metadata["issueReportId"]);
        Assert.Equal("Technical", entries[0].Metadata["category"]);
        Assert.DoesNotContain(entries.SelectMany(e => e.Metadata.Values), v => v.Contains("private words", StringComparison.Ordinal));
    }
}
