using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// The item analysis CSV export (FR-38) over real HTTP and a real database: the file is the page's figures, it is downloaded as an attachment,
/// and each export is in the audit log with who took it and of which exam (FR-40).
/// </summary>
public sealed class ItemAnalysisExportFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
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

    /// <summary>The audit entries recorded against one exam, read through the admin audit route.</summary>
    private static async Task<List<JsonElement>> AuditEntriesAsync(HttpClient admin, Guid examId)
    {
        var response = await admin.GetAsync($"/v1/admin/audit-logs?entityType=Exam&pageSize=100");
        response.EnsureSuccessStatusCode();
        var entries = (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToList();
        return entries.Where(e => e.GetProperty("entityId").GetString() == examId.ToString()).ToList();
    }

    [Fact]
    public async Task AnExport_IsACsvAttachment_WithTheFiguresAndAHeader()
    {
        var (admin, examId) = await OpenExamAsync("Export exam");
        using var _a = admin;

        var response = await admin.PostAsync($"/v1/exams/{examId}/analytics/items/exports", content: null);

        response.EnsureSuccessStatusCode();
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.StartsWith("item-analysis-export-exam-", response.Content.Headers.ContentDisposition?.FileName?.Trim('"') ?? string.Empty);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3));
        var text = Encoding.UTF8.GetString(bytes.AsSpan(3));
        Assert.StartsWith("No.,Question,Attempts,Correct,Difficulty (%),Discrimination,Status\r\n", text);
        Assert.Contains("Q1", text);
    }

    [Fact]
    public async Task AnExport_IsRecordedInTheAuditLog_WithWhoTookItAndOfWhichExam()
    {
        var (admin, examId) = await OpenExamAsync("Audited export");
        using var _a = admin;
        (await admin.PostAsync($"/v1/exams/{examId}/analytics/items/exports", content: null)).EnsureSuccessStatusCode();

        var entry = Assert.Single(await AuditEntriesAsync(admin, examId));

        Assert.Equal("Analytics.ReportExported", entry.GetProperty("action").GetString());
        Assert.Equal("Exam", entry.GetProperty("entityType").GetString());
        Assert.NotEqual(JsonValueKind.Null, entry.GetProperty("actorUserId").ValueKind);
        var metadata = entry.GetProperty("metadata");
        Assert.Equal("item-analysis", metadata.GetProperty("report").GetString());
        Assert.Equal("csv", metadata.GetProperty("format").GetString());
        Assert.Equal("2", metadata.GetProperty("rows").GetString());
    }

    [Fact]
    public async Task AnUnknownExam_IsNotFound_AndNothingIsRecorded()
    {
        using var admin = await factory.AdminClientAsync();
        var missing = Guid.NewGuid();

        var response = await admin.PostAsync($"/v1/exams/{missing}/analytics/items/exports", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await AuditEntriesAsync(admin, missing));
    }
}
