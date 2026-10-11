using System.Net;
using System.Net.Http.Headers;
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
    /// <summary>The audit action every export is recorded under.</summary>
    private const string ExportAction = "Analytics.ReportExported";

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    /// <summary>A SuperAdmin client, and that administrator's id, so the audit log can be read for exactly this administrator's actions.</summary>
    private async Task<(HttpClient Admin, Guid AdminId)> AdminWithIdAsync()
    {
        var signedIn = await factory.SignInAsAsync("SuperAdmin");
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", signedIn.AccessToken);
        return (client, signedIn.UserId);
    }

    /// <summary>A published exam of two questions, open now, made by a fresh administrator.</summary>
    private async Task<(HttpClient Admin, Guid AdminId, Guid ExamId)> OpenExamAsync(string name)
    {
        var (admin, adminId) = await AdminWithIdAsync();
        var questions = new List<Guid>
        {
            await CreateQuestionAsync(admin, "Q1", "Right", "Wrong"),
            await CreateQuestionAsync(admin, "Q2", "Right", "Wrong"),
        };
        var examId = await CreateExamAsync(admin, name, questions, TimeSpan.FromMinutes(-5));
        return (admin, adminId, examId);
    }

    /// <summary>
    /// The export entries for one exam, made by one administrator. The query narrows by entity type and actor; the code then keeps only this
    /// exam's entries and only the export action, because exam authoring writes its own entries (created, published, scheduled) against the
    /// same exam id, and those are not exports.
    /// </summary>
    private static async Task<List<JsonElement>> ExportEntriesAsync(HttpClient admin, Guid examId, Guid adminId)
    {
        var response = await admin.GetAsync($"/v1/admin/audit-logs?entityType=Exam&actorUserId={adminId}&pageSize=100");
        response.EnsureSuccessStatusCode();
        var entries = (await JsonAsync(response)).EnumerateArray().ToList();
        return entries
            .Where(e => e.GetProperty("entityId").GetString() == examId.ToString() && e.GetProperty("action").GetString() == ExportAction)
            .ToList();
    }

    [Fact]
    public async Task AnExport_IsACsvAttachment_WithTheFiguresAndAHeader()
    {
        var (admin, _, examId) = await OpenExamAsync("Export exam");
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
        var (admin, adminId, examId) = await OpenExamAsync("Audited export");
        using var _a = admin;
        (await admin.PostAsync($"/v1/exams/{examId}/analytics/items/exports", content: null)).EnsureSuccessStatusCode();

        var entry = Assert.Single(await ExportEntriesAsync(admin, examId, adminId));

        Assert.Equal(ExportAction, entry.GetProperty("action").GetString());
        Assert.Equal("Exam", entry.GetProperty("entityType").GetString());
        Assert.Equal(examId.ToString(), entry.GetProperty("entityId").GetString());
        Assert.Equal(adminId, entry.GetProperty("actorUserId").GetGuid());
        var metadata = entry.GetProperty("metadata");
        Assert.Equal("item-analysis", metadata.GetProperty("report").GetString());
        Assert.Equal("csv", metadata.GetProperty("format").GetString());
        Assert.Equal("2", metadata.GetProperty("rows").GetString());
    }

    [Fact]
    public async Task AnUnknownExam_IsNotFound_AndNothingIsRecorded()
    {
        var (admin, adminId) = await AdminWithIdAsync();
        using var _a = admin;
        var missing = Guid.NewGuid();

        var response = await admin.PostAsync($"/v1/exams/{missing}/analytics/items/exports", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await ExportEntriesAsync(admin, missing, adminId));
    }
}
