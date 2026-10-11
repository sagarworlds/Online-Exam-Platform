using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// The candidate's own analytics exports (FR-38) over real HTTP and a real database: the CSV and PDF files hold the candidate's released figures
/// and nothing about anyone else, and each export is in the audit log with the candidate as the one who took it (FR-40).
/// </summary>
public sealed class CandidateAnalyticsExportFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    /// <summary>The audit action every export is recorded under.</summary>
    private const string ExportAction = "Analytics.ReportExported";

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    /// <summary>A published exam of two questions, open now, with an administrator client.</summary>
    private async Task<(HttpClient Admin, Guid ExamId)> OpenExamAsync(string name)
    {
        var admin = await factory.AdminClientAsync();
        var questions = new List<Guid>
        {
            await CreateQuestionAsync(admin, "Q1", "Right", "Wrong"),
            await CreateQuestionAsync(admin, "Q2", "Right", "Wrong"),
        };
        var examId = await CreateExamAsync(admin, name, questions, TimeSpan.FromMinutes(-5));
        return (admin, examId);
    }

    /// <summary>Starts an attempt, answers Q1 with the right option, and submits it.</summary>
    private static async Task SitRightOnFirstAsync(HttpClient candidate, Guid examId)
    {
        var started = await candidate.PostAsJsonAsync($"/v1/me/exams/{examId}/attempts", new { instructionsAcknowledged = true });
        started.EnsureSuccessStatusCode();
        var attemptId = (await JsonAsync(started)).GetProperty("id").GetGuid();

        var attempt = await candidate.GetFromJsonAsync<JsonElement>($"/v1/me/attempts/{attemptId}");
        var first = attempt.GetProperty("sections").EnumerateArray().SelectMany(s => s.GetProperty("questions").EnumerateArray())
            .Single(q => q.GetProperty("text").GetString() == "Q1");
        var right = first.GetProperty("options").EnumerateArray().Single(o => o.GetProperty("text").GetString() == "Right").GetProperty("id").GetGuid();
        (await candidate.PutAsJsonAsync($"/v1/me/attempts/{attemptId}/answers/{first.GetProperty("id").GetGuid()}", new { optionId = right }))
            .EnsureSuccessStatusCode();
        (await candidate.PostAsync($"/v1/me/attempts/{attemptId}/submit", content: null)).EnsureSuccessStatusCode();
    }

    /// <summary>The export entries one candidate made, read from the audit log by an administrator.</summary>
    private static async Task<List<JsonElement>> ExportEntriesOfAsync(HttpClient admin, Guid candidateId)
    {
        var response = await admin.GetAsync($"/v1/admin/audit-logs?entityType=Candidate&actorUserId={candidateId}&pageSize=100");
        response.EnsureSuccessStatusCode();
        return (await JsonAsync(response)).EnumerateArray().Where(e => e.GetProperty("action").GetString() == ExportAction).ToList();
    }

    [Fact]
    public async Task ACsvExport_IsAnAttachment_WithTheCandidatesFigures_AndNoIdentity()
    {
        var (admin, examId) = await OpenExamAsync("Candidate CSV export exam");
        using var _a = admin;
        var (candidate, user) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;
        await SitRightOnFirstAsync(candidate, examId);

        var response = await candidate.PostAsync("/v1/me/analytics/exports/csv", content: null);

        response.EnsureSuccessStatusCode();
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        var fileName = response.Content.Headers.ContentDisposition?.FileName?.Trim('"') ?? string.Empty;
        Assert.StartsWith("candidate-analytics-", fileName, StringComparison.Ordinal);
        Assert.EndsWith(".csv", fileName, StringComparison.Ordinal);

        var text = Encoding.UTF8.GetString((await response.Content.ReadAsByteArrayAsync()).AsSpan(3));
        Assert.StartsWith("Exam,Submitted (UTC),Score,Out of,Percent of marks\r\n", text, StringComparison.Ordinal);
        Assert.Contains("Candidate CSV export exam,", text, StringComparison.Ordinal);
        Assert.DoesNotContain(user.Email, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(user.UserId.ToString(), text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task APdfExport_IsAPdfAttachment_WithTheExamOnTheTrend()
    {
        var (admin, examId) = await OpenExamAsync("Candidate PDF export exam");
        using var _a = admin;
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;
        await SitRightOnFirstAsync(candidate, examId);

        var response = await candidate.PostAsync("/v1/me/analytics/exports/pdf", content: null);

        response.EnsureSuccessStatusCode();
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        var text = Encoding.Latin1.GetString(await response.Content.ReadAsByteArrayAsync());
        Assert.StartsWith("%PDF-1.4", text, StringComparison.Ordinal);
        Assert.Contains("(Candidate PDF export exam) Tj", text, StringComparison.Ordinal);
        Assert.Contains("(Score trend) Tj", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACandidateWithNoReleasedResult_GetsAFile_ThatSaysSo()
    {
        var (admin, examId) = await OpenExamAsync("Candidate not sat exam");
        using var _a = admin;
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;

        var response = await candidate.PostAsync("/v1/me/analytics/exports/pdf", content: null);

        response.EnsureSuccessStatusCode();
        var text = Encoding.Latin1.GetString(await response.Content.ReadAsByteArrayAsync());
        Assert.Contains("No results have been released to you yet", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnExport_IsRecordedInTheAuditLog_WithTheCandidateAsTheActor()
    {
        var (admin, examId) = await OpenExamAsync("Candidate audited export exam");
        using var _a = admin;
        var (candidate, user) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;
        await SitRightOnFirstAsync(candidate, examId);
        (await candidate.PostAsync("/v1/me/analytics/exports/csv", content: null)).EnsureSuccessStatusCode();

        var entry = Assert.Single(await ExportEntriesOfAsync(admin, user.UserId));

        Assert.Equal(user.UserId, entry.GetProperty("actorUserId").GetGuid());
        Assert.Equal("Candidate", entry.GetProperty("entityType").GetString());
        Assert.Equal(user.UserId.ToString(), entry.GetProperty("entityId").GetString());
        var metadata = entry.GetProperty("metadata");
        Assert.Equal("candidate-analytics", metadata.GetProperty("report").GetString());
        Assert.Equal("csv", metadata.GetProperty("format").GetString());
        Assert.Equal("1", metadata.GetProperty("results").GetString());
    }

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        using var anonymous = factory.CreateClient();

        var csv = await anonymous.PostAsync("/v1/me/analytics/exports/csv", content: null);
        var pdf = await anonymous.PostAsync("/v1/me/analytics/exports/pdf", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, csv.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, pdf.StatusCode);
    }
}
