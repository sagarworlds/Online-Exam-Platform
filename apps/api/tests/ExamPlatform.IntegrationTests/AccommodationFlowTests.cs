using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExamPlatform.Modules.Admin.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// FR-49 over real HTTP and a real database: staff give a candidate extra time, a reader or scribe and alternate formats; the deadline the
/// server holds includes the time, the candidate is told what applies, a screen reader candidate is not counted out for leaving the page,
/// and the staff note never reaches the candidate.
/// </summary>
public sealed class AccommodationFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await JsonAsync(response)).GetProperty("title").GetString());
    }

    /// <summary>A published one-hour exam, an administrator, and a candidate who accepted the invitation.</summary>
    private async Task<(HttpClient Admin, HttpClient Candidate, Guid CandidateId, Guid ExamId)> EnrolledAsync(string name)
    {
        var admin = await factory.AdminClientAsync();
        var question = await CreateQuestionAsync(admin, $"{name} question", "Right", "Wrong");
        var examId = await CreateExamAsync(admin, name, [question], TimeSpan.FromMinutes(-5), durationMinutes: 60);
        var (candidate, user) = await factory.EnrollNewCandidateAsync(admin, examId);
        return (admin, candidate, user.UserId, examId);
    }

    private static string Route(Guid examId, Guid candidateId) => $"/v1/exams/{examId}/candidates/{candidateId}/accommodation";

    private static Task<HttpResponseMessage> SetAsync(
        HttpClient admin, Guid examId, Guid candidateId, int minutes = 30, bool scribe = false, string[]? formats = null, string? notes = null) =>
        admin.PutAsJsonAsync(Route(examId, candidateId), new { extraTimeMinutes = minutes, readerScribe = scribe, alternateFormats = formats ?? [], notes });

    private static async Task<JsonElement> StartAsync(HttpClient candidate, Guid examId) =>
        await JsonAsync((await candidate.PostAsJsonAsync($"/v1/me/exams/{examId}/attempts", new { instructionsAcknowledged = true })).EnsureSuccessStatusCode());

    private static TimeSpan Allowed(JsonElement attempt) => attempt.GetProperty("deadlineUtc").GetDateTime() - attempt.GetProperty("startedAtUtc").GetDateTime();

    [Fact]
    public async Task AnAccommodation_GivesTheCandidateExtraTime_InTheDeadlineTheServerHolds_AndTellsThemWhatApplies()
    {
        var (admin, candidate, candidateId, examId) = await EnrolledAsync("Accommodation deadline exam");
        using var _a = admin;
        using var _c = candidate;
        var set = await SetAsync(admin, examId, candidateId, 30, scribe: true, ["large_text", "high_contrast"], "Certificate seen");
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);

        var attempt = await StartAsync(candidate, examId);

        // An hour, and half an hour on top: the end time is the server's, so the page only shows it.
        Assert.InRange(Allowed(attempt).TotalSeconds, 5399, 5401);
        var told = attempt.GetProperty("accommodation");
        Assert.Equal(1800, told.GetProperty("extraTimeSeconds").GetInt32());
        Assert.True(told.GetProperty("readerScribe").GetBoolean());
        Assert.Equal(["large_text", "high_contrast"], told.GetProperty("alternateFormats").EnumerateArray().Select(f => f.GetString()));
    }

    [Fact]
    public async Task AnotherCandidate_GetsTheExamsOwnTime_AndNoAccommodation()
    {
        var (admin, candidate, candidateId, examId) = await EnrolledAsync("Accommodation neighbour exam");
        using var _a = admin;
        using var _c = candidate;
        await SetAsync(admin, examId, candidateId, 30);
        var (other, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _o = other;

        var attempt = await StartAsync(other, examId);

        Assert.InRange(Allowed(attempt).TotalSeconds, 3599, 3601);
        Assert.Equal(JsonValueKind.Null, attempt.GetProperty("accommodation").ValueKind);
    }

    [Fact]
    public async Task OnlyStaffEverSeeTheNote_NotTheCandidateOnTheirExamsOrInTheirAttempt()
    {
        var (admin, candidate, candidateId, examId) = await EnrolledAsync("Accommodation note exam");
        using var _a = admin;
        using var _c = candidate;
        await SetAsync(admin, examId, candidateId, 15, notes: "Certificate seen, reference 4471");

        var staff = (await admin.GetFromJsonAsync<JsonElement>($"/v1/exams/{examId}/attempts")).GetProperty("candidates").EnumerateArray()
            .Single(c => c.GetProperty("candidateId").GetGuid() == candidateId).GetProperty("accommodation");
        Assert.Equal("Certificate seen, reference 4471", staff.GetProperty("notes").GetString());
        Assert.Equal(15, staff.GetProperty("extraTimeMinutes").GetInt32());

        var attempt = (await candidate.PostAsJsonAsync($"/v1/me/exams/{examId}/attempts", new { instructionsAcknowledged = true })).EnsureSuccessStatusCode();
        var mine = await candidate.GetStringAsync("/v1/me/exams");
        Assert.DoesNotContain("reference 4471", mine);
        Assert.DoesNotContain("reference 4471", await attempt.Content.ReadAsStringAsync());
        Assert.Contains("\"extraTimeSeconds\":900", mine);
    }

    [Fact]
    public async Task ExtraTime_ReachesAnAttemptAlreadyInProgress_ThroughTheHeartbeat()
    {
        var (admin, candidate, candidateId, examId) = await EnrolledAsync("Accommodation mid-exam");
        using var _a = admin;
        using var _c = candidate;
        var attempt = await StartAsync(candidate, examId);
        var attemptId = attempt.GetProperty("id").GetGuid();
        var before = attempt.GetProperty("deadlineUtc").GetDateTime();

        (await SetAsync(admin, examId, candidateId, 20, formats: ["large_text"])).EnsureSuccessStatusCode();

        var status = await candidate.GetFromJsonAsync<JsonElement>($"/v1/me/attempts/{attemptId}/status");
        Assert.InRange((status.GetProperty("deadlineUtc").GetDateTime() - before).TotalSeconds, 1199, 1201);
        var reread = await candidate.GetFromJsonAsync<JsonElement>($"/v1/me/attempts/{attemptId}");
        Assert.Equal(1200, reread.GetProperty("accommodation").GetProperty("extraTimeSeconds").GetInt32());
    }

    [Fact]
    public async Task ChangingTheExtraTime_MidExam_AddsOnlyTheDifference_AndLoweringItTakesNothingBack()
    {
        var (admin, candidate, candidateId, examId) = await EnrolledAsync("Accommodation change exam");
        using var _a = admin;
        using var _c = candidate;
        var attempt = await StartAsync(candidate, examId);
        var attemptId = attempt.GetProperty("id").GetGuid();
        var original = attempt.GetProperty("deadlineUtc").GetDateTime();
        await SetAsync(admin, examId, candidateId, 20);

        await SetAsync(admin, examId, candidateId, 45);
        var raised = (await candidate.GetFromJsonAsync<JsonElement>($"/v1/me/attempts/{attemptId}/status")).GetProperty("deadlineUtc").GetDateTime();
        await SetAsync(admin, examId, candidateId, 10);
        var lowered = (await candidate.GetFromJsonAsync<JsonElement>($"/v1/me/attempts/{attemptId}/status")).GetProperty("deadlineUtc").GetDateTime();

        Assert.InRange((raised - original).TotalSeconds, 2699, 2701);
        Assert.Equal(raised, lowered);
    }

    [Fact]
    public async Task RemovingIt_LeavesTheRowWithoutOne_AndAnAttemptInProgressKeepsItsTime()
    {
        var (admin, candidate, candidateId, examId) = await EnrolledAsync("Accommodation remove exam");
        using var _a = admin;
        using var _c = candidate;
        await SetAsync(admin, examId, candidateId, 30);
        var attempt = await StartAsync(candidate, examId);

        var removed = await admin.DeleteAsync(Route(examId, candidateId));

        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await JsonAsync(removed)).GetProperty("accommodation").ValueKind);
        var status = await candidate.GetFromJsonAsync<JsonElement>($"/v1/me/attempts/{attempt.GetProperty("id").GetGuid()}/status");
        // Within a second: the start response carries the instant as .NET holds it, the heartbeat as the database stored it (to the microsecond).
        Assert.InRange(Math.Abs((attempt.GetProperty("deadlineUtc").GetDateTime() - status.GetProperty("deadlineUtc").GetDateTime()).TotalSeconds), 0, 1);
        await AssertProblemAsync(await admin.DeleteAsync(Route(examId, candidateId)), HttpStatusCode.NotFound, "accommodation_not_found");
    }

    [Theory]
    [InlineData(0, false, new string[0])]
    [InlineData(721, false, new string[0])]
    [InlineData(-5, false, new string[0])]
    [InlineData(30, false, new[] { "braille" })]
    public async Task AnAccommodationThatGivesNothingOrIsOutOfRange_IsRefused(int minutes, bool scribe, string[] formats)
    {
        var (admin, candidate, candidateId, examId) = await EnrolledAsync($"Accommodation refused {minutes} {string.Join(",", formats)}");
        using var _a = admin;
        using var _c = candidate;

        await AssertProblemAsync(await SetAsync(admin, examId, candidateId, minutes, scribe, formats), HttpStatusCode.BadRequest, "invalid_accommodation");
    }

    [Fact]
    public async Task AccommodationsCanOnlyBeSetForAnEnrolledCandidateOfARealExam()
    {
        var (admin, candidate, candidateId, examId) = await EnrolledAsync("Accommodation enrolment exam");
        using var _a = admin;
        using var _c = candidate;

        await AssertProblemAsync(await SetAsync(admin, examId, Guid.NewGuid()), HttpStatusCode.NotFound, "candidate_not_enrolled");
        await AssertProblemAsync(await SetAsync(admin, Guid.NewGuid(), candidateId), HttpStatusCode.NotFound, "exam_not_found");
        await AssertProblemAsync(await admin.DeleteAsync(Route(examId, Guid.NewGuid())), HttpStatusCode.NotFound, "candidate_not_enrolled");
    }

    [Fact]
    public async Task ACandidateCannotSetTheirOwnAccommodation()
    {
        var (admin, candidate, candidateId, examId) = await EnrolledAsync("Accommodation self-service exam");
        using var _a = admin;
        using var _c = candidate;

        var attempt = await SetAsync(candidate, examId, candidateId, 60);

        Assert.Equal(HttpStatusCode.Forbidden, attempt.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await candidate.DeleteAsync(Route(examId, candidateId))).StatusCode);
    }

    [Fact]
    public async Task ACandidateWithAScreenReader_IsNotCountedOutForLeavingThePage_ButAnotherIsEnded()
    {
        var (admin, candidate, candidateId, examId) = await EnrolledAsync("Accommodation screen reader exam");
        using var _a = admin;
        using var _c = candidate;
        (await admin.PutAsJsonAsync($"/v1/exams/{examId}/focus-violation-limit", new { focusViolationLimit = 2 })).EnsureSuccessStatusCode();
        await SetAsync(admin, examId, candidateId, 0, formats: ["screen_reader"]);
        var (other, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _o = other;

        var readerAttempt = await StartAsync(candidate, examId);
        var otherAttempt = await StartAsync(other, examId);

        // Told the truth about the rules they sit under: no limit for the reader, the exam's limit for the other.
        Assert.Equal(0, readerAttempt.GetProperty("focusViolationLimit").GetInt32());
        Assert.Equal(2, otherAttempt.GetProperty("focusViolationLimit").GetInt32());
        var mine = (await candidate.GetFromJsonAsync<JsonElement>("/v1/me/exams")).EnumerateArray().Single(e => e.GetProperty("examId").GetGuid() == examId);
        Assert.Equal(0, mine.GetProperty("rules").GetProperty("focusViolationLimit").GetInt32());

        for (var i = 0; i < 4; i++)
        {
            var left = await candidate.PostAsJsonAsync($"/v1/me/attempts/{readerAttempt.GetProperty("id").GetGuid()}/focus-violations", new { kind = "TabHidden" });
            Assert.False((await JsonAsync(left.EnsureSuccessStatusCode())).GetProperty("attemptEnded").GetBoolean());
        }

        Assert.Equal("InProgress", (await candidate.GetFromJsonAsync<JsonElement>($"/v1/me/attempts/{readerAttempt.GetProperty("id").GetGuid()}/status")).GetProperty("status").GetString());
        var otherId = otherAttempt.GetProperty("id").GetGuid();
        (await other.PostAsJsonAsync($"/v1/me/attempts/{otherId}/focus-violations", new { kind = "TabHidden" })).EnsureSuccessStatusCode();
        var ended = await JsonAsync((await other.PostAsJsonAsync($"/v1/me/attempts/{otherId}/focus-violations", new { kind = "TabHidden" })).EnsureSuccessStatusCode());
        Assert.True(ended.GetProperty("attemptEnded").GetBoolean());
    }

    [Fact]
    public async Task SettingAndTakingOnAnAccommodation_AreAudited_WithoutTheNote()
    {
        var (admin, candidate, candidateId, examId) = await EnrolledAsync("Accommodation audit exam");
        using var _a = admin;
        using var _c = candidate;
        await SetAsync(admin, examId, candidateId, 25, scribe: true, ["screen_reader"], "Certificate seen, reference 9921");
        var attempt = await StartAsync(candidate, examId);
        await admin.DeleteAsync(Route(examId, candidateId));

        using var scope = factory.Services.CreateScope();
        // The candidate is found in memory: the metadata is a dictionary the database cannot be asked about.
        var entries = (await scope.ServiceProvider.GetRequiredService<AdminDbContext>().AuditLogs.AsNoTracking()
                .Where(a => a.Action.StartsWith("ExamRuntime.Accommodation") || a.Action == "ExamRuntime.AttemptAccommodated")
                .OrderBy(a => a.OccurredAtUtc)
                .ToListAsync())
            .Where(a => a.Metadata["candidateId"] == candidateId.ToString())
            .ToList();

        Assert.Equal(["ExamRuntime.AccommodationSet", "ExamRuntime.AttemptAccommodated", "ExamRuntime.AccommodationRemoved"], entries.Select(e => e.Action).ToArray());
        var set = entries[0];
        Assert.Equal(("1500", "true", "screen_reader"), (set.Metadata["extraTimeSeconds"], set.Metadata["readerScribe"], set.Metadata["alternateFormats"]));
        Assert.Equal(attempt.GetProperty("id").GetGuid().ToString(), entries[1].EntityId);
        Assert.Equal("1500", entries[1].Metadata["addedSeconds"]);
        Assert.All(entries, e => Assert.DoesNotContain(e.Metadata.Values, v => v.Contains("reference 9921")));
        Assert.Equal(examId.ToString(), set.Metadata["examId"]);
    }
}
