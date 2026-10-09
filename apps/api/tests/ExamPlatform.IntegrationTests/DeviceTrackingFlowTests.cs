using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExamPlatform.Modules.Admin.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// Where a candidate sits an exam from (FR-26): their address and device signature are kept with the attempt, a change of either is
/// recorded and audited, and a second sign-in that ends the first session is audited with both places.
/// </summary>
public sealed class DeviceTrackingFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record Sitting(HttpClient Admin, HttpClient Candidate, Guid ExamId, Guid AttemptId);

    private static HttpRequestMessage From(HttpMethod method, string url, string? device, string? ip, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (device is not null)
            request.Headers.Add("X-Device-Fingerprint", device);
        if (ip is not null)
            request.Headers.Add(TestRemoteIpStartupFilter.HeaderName, ip);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return request;
    }

    private async Task<Sitting> StartAsync(string? device, string? ip)
    {
        var admin = await factory.AdminClientAsync();
        var question = await CreateQuestionAsync(admin, "What is 2 + 2?", "4", "5");
        var examId = await CreateExamAsync(admin, "Device Exam", [question], startsIn: TimeSpan.FromMinutes(-5));
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);

        var response = await candidate.SendAsync(From(HttpMethod.Post, $"/v1/me/exams/{examId}/attempts", device, ip, new { instructionsAcknowledged = true }));
        response.EnsureSuccessStatusCode();
        var attempt = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new Sitting(admin, candidate, examId, attempt.GetProperty("id").GetGuid());
    }

    private static async Task<JsonElement> ClientsAsync(Sitting s) =>
        await s.Admin.GetFromJsonAsync<JsonElement>($"/v1/exams/{s.ExamId}/attempts/{s.AttemptId}/clients");

    private static async Task<JsonElement> StaffRowAsync(Sitting s)
    {
        var staff = await s.Admin.GetFromJsonAsync<JsonElement>($"/v1/exams/{s.ExamId}/attempts");
        return staff.GetProperty("candidates").EnumerateArray().SelectMany(c => c.GetProperty("attempts").EnumerateArray()).Single();
    }

    [Fact]
    public async Task StartingAnAttempt_KeepsWhereTheCandidateBegan()
    {
        var s = await StartAsync("devicea0000000001", "203.0.113.10");
        using var _a = s.Admin;
        using var _c = s.Candidate;

        var seen = Assert.Single((await ClientsAsync(s)).EnumerateArray());

        Assert.Equal("203.0.113.10", seen.GetProperty("ipAddress").GetString());
        Assert.Equal("devicea0000000001", seen.GetProperty("deviceFingerprint").GetString());
        Assert.Equal("Started", seen.GetProperty("reason").GetString());
        var row = await StaffRowAsync(s);
        Assert.Equal(0, row.GetProperty("clientChanges").GetInt32());
        Assert.Equal(1, row.GetProperty("devices").GetInt32());
    }

    [Fact]
    public async Task LoadingTheAttemptFromAnotherDevice_IsRecordedAsAChange_AndAudited()
    {
        var s = await StartAsync("devicea0000000001", "203.0.113.10");
        using var _a = s.Admin;
        using var _c = s.Candidate;

        (await s.Candidate.SendAsync(From(HttpMethod.Get, $"/v1/me/attempts/{s.AttemptId}", "deviceb0000000002", "198.51.100.7"))).EnsureSuccessStatusCode();

        var rows = (await ClientsAsync(s)).EnumerateArray().ToList();
        Assert.Equal(["Started", "Changed"], rows.Select(r => r.GetProperty("reason").GetString()));
        Assert.Equal("deviceb0000000002", rows[1].GetProperty("deviceFingerprint").GetString());
        var row = await StaffRowAsync(s);
        Assert.Equal(1, row.GetProperty("clientChanges").GetInt32());
        Assert.Equal(2, row.GetProperty("devices").GetInt32());

        using var scope = factory.Services.CreateScope();
        var entries = await scope.ServiceProvider.GetRequiredService<AdminDbContext>().AuditLogs.AsNoTracking()
            .Where(a => a.EntityId == s.AttemptId.ToString() && a.Action == "ExamRuntime.AttemptClientChanged")
            .ToListAsync();
        var entry = Assert.Single(entries);
        Assert.Equal("devicea0000000001", entry.Metadata["previousDevice"]);
        Assert.Equal("deviceb0000000002", entry.Metadata["device"]);
        Assert.Equal("203.0.113.10", entry.Metadata["previousIp"]);
        Assert.Equal("198.51.100.7", entry.Metadata["ip"]);
    }

    [Fact]
    public async Task TheHeartbeat_NoticesAChangeToo()
    {
        var s = await StartAsync("devicea0000000001", "203.0.113.10");
        using var _a = s.Admin;
        using var _c = s.Candidate;

        (await s.Candidate.SendAsync(From(HttpMethod.Get, $"/v1/me/attempts/{s.AttemptId}/status", "devicea0000000001", "198.51.100.7"))).EnsureSuccessStatusCode();

        Assert.Equal(1, (await StaffRowAsync(s)).GetProperty("clientChanges").GetInt32());
    }

    [Fact]
    public async Task StayingPut_AddsNothing()
    {
        var s = await StartAsync("devicea0000000001", "203.0.113.10");
        using var _a = s.Admin;
        using var _c = s.Candidate;

        for (var i = 0; i < 3; i++)
            (await s.Candidate.SendAsync(From(HttpMethod.Get, $"/v1/me/attempts/{s.AttemptId}/status", "devicea0000000001", "203.0.113.10"))).EnsureSuccessStatusCode();

        Assert.Single((await ClientsAsync(s)).EnumerateArray());
    }

    [Theory]
    [InlineData("has spaces in it")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("semi;colon")]
    public async Task ASignatureNotInTheExpectedForm_IsNotStored_SoItIsTheSameAsSendingNone(string device)
    {
        var s = await StartAsync(device: null, ip: "203.0.113.10");
        using var _a = s.Admin;
        using var _c = s.Candidate;

        (await s.Candidate.SendAsync(From(HttpMethod.Get, $"/v1/me/attempts/{s.AttemptId}", device, "203.0.113.10"))).EnsureSuccessStatusCode();

        // Started with none, and the odd one cleaned away to none: nothing changed, and nothing odd was kept.
        var seen = Assert.Single((await ClientsAsync(s)).EnumerateArray());
        Assert.Equal(JsonValueKind.Null, seen.GetProperty("deviceFingerprint").ValueKind);
    }

    [Fact]
    public async Task OpeningTheResultFromAnotherDevice_IsNotAChange()
    {
        var s = await StartAsync("devicea0000000001", "203.0.113.10");
        using var _a = s.Admin;
        using var _c = s.Candidate;
        (await s.Candidate.SendAsync(From(HttpMethod.Post, $"/v1/me/attempts/{s.AttemptId}/submit", "devicea0000000001", "203.0.113.10"))).EnsureSuccessStatusCode();

        (await s.Candidate.SendAsync(From(HttpMethod.Get, $"/v1/me/attempts/{s.AttemptId}", "deviceb0000000002", "198.51.100.7"))).EnsureSuccessStatusCode();

        Assert.Single((await ClientsAsync(s)).EnumerateArray());
    }

    [Fact]
    public async Task OnlyStaffCanSeeWhereAnAttemptWasSat_AndOnlyOfTheirOwnExam()
    {
        var s = await StartAsync("devicea0000000001", "203.0.113.10");
        using var _a = s.Admin;
        using var _c = s.Candidate;

        Assert.Equal(HttpStatusCode.Forbidden, (await s.Candidate.GetAsync($"/v1/exams/{s.ExamId}/attempts/{s.AttemptId}/clients")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Admin.GetAsync($"/v1/exams/{Guid.NewGuid()}/attempts/{s.AttemptId}/clients")).StatusCode);
        var mine = (await s.Candidate.GetFromJsonAsync<JsonElement>("/v1/me/exams")).EnumerateArray().Single(e => e.GetProperty("examId").GetGuid() == s.ExamId);
        var summary = mine.GetProperty("attempts")[0];
        Assert.Equal(JsonValueKind.Null, summary.GetProperty("clientChanges").ValueKind);
        Assert.Equal(JsonValueKind.Null, summary.GetProperty("devices").ValueKind);
    }

    [Fact]
    public async Task ASecondSignIn_ThatEndsTheFirstSession_IsAuditedWithBothPlaces()
    {
        const string password = "candidate-password-1";
        var candidate = await factory.SignInAsAsync("Candidate", password: password);
        using var client = factory.CreateClient();

        async Task<HttpResponseMessage> LoginAsync(string device, string ip) =>
            await client.SendAsync(From(HttpMethod.Post, "/v1/auth/login", device, ip, new { email = candidate.Email, password }));

        (await LoginAsync("devicea0000000001", "203.0.113.10")).EnsureSuccessStatusCode();
        (await LoginAsync("deviceb0000000002", "198.51.100.7")).EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var entries = await scope.ServiceProvider.GetRequiredService<AdminDbContext>().AuditLogs.AsNoTracking()
            .Where(a => a.EntityId == candidate.UserId.ToString() && a.Action == "Identity.SessionSuperseded")
            .OrderBy(a => a.OccurredAtUtc)
            .ToListAsync();

        Assert.Equal(2, entries.Count);
        var second = entries[^1];
        Assert.Equal(candidate.UserId, second.ActorUserId);
        Assert.Equal("devicea0000000001", second.Metadata["supersededDevice"]);
        Assert.Equal("203.0.113.10", second.Metadata["supersededIp"]);
        Assert.Equal("deviceb0000000002", second.Metadata["newDevice"]);
        Assert.Equal("198.51.100.7", second.Metadata["newIp"]);
    }
}
