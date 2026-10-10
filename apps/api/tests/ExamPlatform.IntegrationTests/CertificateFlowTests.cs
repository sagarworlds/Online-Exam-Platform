using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// The PDF certificate (FR-34) over real HTTP and a real database: a candidate gets a PDF for a result that has been released, and nobody gets
/// one before that, or for someone else's attempt.
/// </summary>
public sealed class CertificateFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await JsonAsync(response)).GetProperty("title").GetString());
    }

    private static async Task<Guid> SitAsync(HttpClient candidate, Guid examId, Guid questionId)
    {
        var started = await candidate.PostAsJsonAsync($"/v1/me/exams/{examId}/attempts", new { instructionsAcknowledged = true });
        var attemptId = (await JsonAsync(started.EnsureSuccessStatusCode())).GetProperty("id").GetGuid();
        (await candidate.PostAsync($"/v1/me/attempts/{attemptId}/submit", content: null)).EnsureSuccessStatusCode();
        return attemptId;
    }

    [Fact]
    public async Task OnceReleased_TheCandidateGetsAPdf_ForTheirOwnSubmittedAttempt()
    {
        var admin = await factory.AdminClientAsync();
        using var _a = admin;
        var question = await CreateQuestionAsync(admin, "Certificate question", "Right", "Wrong");
        var examId = await CreateExamAsync(admin, "Certified exam", new List<Guid> { question }, TimeSpan.FromMinutes(-5));
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;
        var attemptId = await SitAsync(candidate, examId, question);

        var response = (await candidate.GetAsync($"/v1/me/attempts/{attemptId}/certificate")).EnsureSuccessStatusCode();

        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.StartsWith("%PDF-1.4", Encoding.Latin1.GetString(bytes, 0, 8), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BeforeTheResultIsReleased_NoCertificate()
    {
        var admin = await factory.AdminClientAsync();
        using var _a = admin;
        var question = await CreateQuestionAsync(admin, "Held certificate question", "Right", "Wrong");
        var examId = await CreateExamAsync(admin, "Held certificate exam", new List<Guid> { question }, TimeSpan.FromMinutes(-5));
        (await admin.PutAsJsonAsync($"/v1/exams/{examId}/result-release", new { mode = "Scheduled", releaseTime = DateTime.UtcNow.AddDays(2) })).EnsureSuccessStatusCode();
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;
        var attemptId = await SitAsync(candidate, examId, question);

        await AssertProblemAsync(await candidate.GetAsync($"/v1/me/attempts/{attemptId}/certificate"), HttpStatusCode.Conflict, "results_not_released");
    }

    [Fact]
    public async Task SomeoneElsesAttempt_HasNoCertificate_ForAnotherCandidateOrAnAdministrator()
    {
        var admin = await factory.AdminClientAsync();
        using var _a = admin;
        var question = await CreateQuestionAsync(admin, "Shared certificate question", "Right", "Wrong");
        var examId = await CreateExamAsync(admin, "Shared certificate exam", new List<Guid> { question }, TimeSpan.FromMinutes(-5));
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _c = candidate;
        var attemptId = await SitAsync(candidate, examId, question);
        var (other, _) = await factory.CandidateClientAsync();
        using var _o = other;

        await AssertProblemAsync(await other.GetAsync($"/v1/me/attempts/{attemptId}/certificate"), HttpStatusCode.NotFound, "attempt_not_found");
        await AssertProblemAsync(await admin.GetAsync($"/v1/me/attempts/{attemptId}/certificate"), HttpStatusCode.NotFound, "attempt_not_found");
    }
}
