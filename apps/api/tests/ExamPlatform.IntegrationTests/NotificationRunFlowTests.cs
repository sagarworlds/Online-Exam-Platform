using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExamPlatform.Modules.ExamRuntime.Application;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Infrastructure;
using ExamPlatform.SharedKernel.Infrastructure.Email;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// The Host with the notification trigger switched on (a run key) and a mail sender that only remembers what it was given, so what a
/// pass of the notification run sends can be read back (FR-39).
/// </summary>
public sealed class NotificationApiFactory : ApiFactory
{
    /// <summary>The key an outside scheduler presents to start a pass.</summary>
    public const string RunKey = "integration-test-notification-run-key";

    /// <summary>The mail sender the host hands every message to.</summary>
    public RecordingMailSender Mail { get; } = new();

    /// <inheritdoc />
    protected override IReadOnlyDictionary<string, string?> AdditionalConfiguration =>
        new Dictionary<string, string?>(base.AdditionalConfiguration) { ["Notifications:RunKey"] = RunKey };

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services => services.AddSingleton<IMailSender>(Mail));
    }
}

/// <summary>
/// The platform's scheduled e-mails (FR-39) over a real database: reminders before an exam, a notice when a result can be seen, a notice
/// when a score is revised, and that none of them is sent twice or sent to a mail server that does not exist.
/// </summary>
public sealed class NotificationRunFlowTests(NotificationApiFactory factory) : IClassFixture<NotificationApiFactory>
{
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private async Task<NotificationRunSummary> RunAsync(DateTime? at = null)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<NotificationRun>().RunAsync(at ?? DateTime.UtcNow, CancellationToken.None);
    }

    private List<OutgoingMail> MailTo(string address) => factory.Mail.Sent.Where(m => m.To == address).ToList();

    private async Task<NotificationDelivery[]> DeliveriesAboutAsync(params Guid[] subjects)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ExamRuntimeDbContext>().NotificationDeliveries
            .AsNoTracking()
            .Where(d => subjects.Contains(d.SubjectId))
            .ToArrayAsync();
    }

    /// <summary>An exam of one question starting <paramref name="startsIn"/> from now, with a candidate enrolled.</summary>
    private sealed record Scheduled(HttpClient Admin, HttpClient Candidate, string Email, Guid ExamId, Guid QuestionId, string ExamName);

    private async Task<Scheduled> ScheduledAsync(TimeSpan startsIn)
    {
        var admin = await factory.AdminClientAsync();
        var questionId = await CreateQuestionAsync(admin, "Capital of France?", "Rome", "Paris");
        var name = $"Geography {Guid.NewGuid():N}";
        var examId = await CreateExamAsync(admin, name, [questionId], startsIn);
        var (candidate, user) = await factory.EnrollNewCandidateAsync(admin, examId);
        return new Scheduled(admin, candidate, user.Email, examId, questionId, name);
    }

    /// <summary>Starts the candidate's attempt, chooses "Paris" (which the exam's wrong key marks wrong) and submits; returns the attempt and the id of "Paris".</summary>
    private static async Task<(Guid AttemptId, Guid Paris)> SitAsync(Scheduled s)
    {
        var started = await s.Candidate.PostAsJsonAsync($"/v1/me/exams/{s.ExamId}/attempts", new { instructionsAcknowledged = true });
        var attemptId = (await JsonAsync(started.EnsureSuccessStatusCode())).GetProperty("id").GetGuid();
        var attempt = await s.Candidate.GetFromJsonAsync<JsonElement>($"/v1/me/attempts/{attemptId}");
        var question = attempt.GetProperty("sections")[0].GetProperty("questions")[0];
        var paris = question.GetProperty("options").EnumerateArray().Single(o => o.GetProperty("text").GetString() == "Paris").GetProperty("id").GetGuid();
        (await s.Candidate.PutAsJsonAsync($"/v1/me/attempts/{attemptId}/answers/{question.GetProperty("id").GetGuid()}", new { optionId = paris })).EnsureSuccessStatusCode();
        (await s.Candidate.PostAsync($"/v1/me/attempts/{attemptId}/submit", content: null)).EnsureSuccessStatusCode();
        return (attemptId, paris);
    }

    // ---- reminders -------------------------------------------------------------------------------------

    [Fact]
    public async Task AnExamStartingWithinADay_RemindsItsCandidateOnce_AndTheRecordSaysSo()
    {
        var s = await ScheduledAsync(TimeSpan.FromHours(20));
        using var _a = s.Admin;
        using var _c = s.Candidate;

        await RunAsync();
        await RunAsync();

        var mail = Assert.Single(MailTo(s.Email));
        Assert.Contains(s.ExamName, mail.Subject);
        Assert.Contains("starts within a day", mail.Body);
        var delivery = Assert.Single(await DeliveriesAboutAsync(s.ExamId));
        Assert.Equal(NotificationKind.ExamReminder24Hours, delivery.Kind);
        Assert.NotNull(delivery.SentAtUtc);
    }

    [Fact]
    public async Task AnExamStartingWithinTheHour_GetsTheCloserReminder()
    {
        var s = await ScheduledAsync(TimeSpan.FromMinutes(30));
        using var _a = s.Admin;
        using var _c = s.Candidate;

        await RunAsync();

        var mail = Assert.Single(MailTo(s.Email));
        Assert.Equal($"{s.ExamName} starts in less than an hour", mail.Subject);
        Assert.Equal(NotificationKind.ExamReminderOneHour, Assert.Single(await DeliveriesAboutAsync(s.ExamId)).Kind);
    }

    [Fact]
    public async Task AnExamMoreThanADayAway_IsNotRemindedYet()
    {
        var s = await ScheduledAsync(TimeSpan.FromHours(40));
        using var _a = s.Admin;
        using var _c = s.Candidate;

        await RunAsync();

        Assert.Empty(MailTo(s.Email));
        Assert.Empty(await DeliveriesAboutAsync(s.ExamId));
    }

    // ---- results and revisions -------------------------------------------------------------------------

    [Fact]
    public async Task ASubmittedAttempt_HasItsResultAnnouncedOnce()
    {
        var s = await ScheduledAsync(TimeSpan.FromMinutes(-5));
        using var _a = s.Admin;
        using var _c = s.Candidate;
        var (attemptId, _) = await SitAsync(s);

        await RunAsync();
        await RunAsync();

        var mail = Assert.Single(MailTo(s.Email));
        Assert.Equal($"Your result for {s.ExamName} is ready", mail.Subject);
        var delivery = Assert.Single(await DeliveriesAboutAsync(attemptId));
        Assert.Equal(NotificationKind.ResultReleased, delivery.Kind);
        Assert.NotNull(delivery.SentAtUtc);
    }

    [Fact]
    public async Task ACorrectedAnswerKey_TellsTheCandidateTheirScoreWasRevised_Once()
    {
        var s = await ScheduledAsync(TimeSpan.FromMinutes(-5));
        using var _a = s.Admin;
        using var _c = s.Candidate;
        var (attemptId, paris) = await SitAsync(s);
        await RunAsync();

        (await s.Admin.PostAsJsonAsync($"/v1/questions/{s.QuestionId}/correct-answer-key",
            new { correctOptionIds = new[] { paris }, reason = "Paris is the capital of France, not Rome" })).EnsureSuccessStatusCode();
        await RunAsync();
        await RunAsync();

        var mails = MailTo(s.Email);
        Assert.Equal(2, mails.Count);
        var revised = mails.Single(m => m.Subject == $"Your score for {s.ExamName} was revised");
        Assert.Contains("changed from 0 out of 1 to 1 out of 1", revised.Body);
        Assert.Contains("Why: Paris is the capital of France, not Rome", revised.Body);

        // One record for the result, and one for the revision, whose subject is the revision itself.
        Assert.Equal(NotificationKind.ResultReleased, Assert.Single(await DeliveriesAboutAsync(attemptId)).Kind);
        var revisionRecord = Assert.Single(await DeliveriesForRevisionsAsync(attemptId));
        Assert.Equal(NotificationKind.ScoreRevised, revisionRecord.Kind);
        Assert.NotNull(revisionRecord.SentAtUtc);
    }

    /// <summary>The records about the revisions of one attempt, whose subject is the revision and not the attempt.</summary>
    private async Task<NotificationDelivery[]> DeliveriesForRevisionsAsync(Guid attemptId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ExamRuntimeDbContext>();
        var revisionIds = await context.Set<AttemptResultRevision>().AsNoTracking().Where(r => r.AttemptId == attemptId).Select(r => r.Id).ToListAsync();
        return await context.NotificationDeliveries.AsNoTracking().Where(d => revisionIds.Contains(d.SubjectId)).ToArrayAsync();
    }

    // ---- the mail server -------------------------------------------------------------------------------

    [Fact]
    public async Task AMailServerThatRefuses_IsTriedAgainOnlyAfterTheDelay_AndThenTheMessageGoes()
    {
        var s = await ScheduledAsync(TimeSpan.FromHours(20));
        using var _a = s.Admin;
        using var _c = s.Candidate;
        var now = DateTime.UtcNow;

        try
        {
            factory.Mail.Delivers = false;
            await RunAsync(now);
            await RunAsync(now.AddMinutes(5));

            var tried = Assert.Single(await DeliveriesAboutAsync(s.ExamId));
            Assert.Equal(1, tried.Attempts);
            Assert.Null(tried.SentAtUtc);

            factory.Mail.Delivers = true;
            await RunAsync(now + NotificationDelivery.RetryAfter);
        }
        finally
        {
            factory.Mail.Delivers = true;
        }

        var sent = Assert.Single(await DeliveriesAboutAsync(s.ExamId));
        Assert.Equal(2, sent.Attempts);
        Assert.NotNull(sent.SentAtUtc);
    }

    [Fact]
    public async Task WithNoMailServerConfigured_NothingIsSentOrRecorded_AndWhatIsDueGoesOnceThereIsOne()
    {
        var s = await ScheduledAsync(TimeSpan.FromHours(20));
        using var _a = s.Admin;
        using var _c = s.Candidate;

        try
        {
            factory.Mail.IsConfigured = false;
            var idle = await RunAsync();

            Assert.False(idle.MailAvailable);
            Assert.Empty(await DeliveriesAboutAsync(s.ExamId));
        }
        finally
        {
            factory.Mail.IsConfigured = true;
        }

        await RunAsync();
        Assert.Single(MailTo(s.Email));
    }
}

/// <summary>The route an outside scheduler calls to start a pass (FR-39): proven by a key, and doing the same work as the timer.</summary>
public sealed class NotificationRunEndpointTests(NotificationApiFactory factory) : IClassFixture<NotificationApiFactory>
{
    private const string Route = "/v1/notifications/run";

    private async Task<HttpResponseMessage> CallAsync(string? key)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, Route);
        if (key is not null)
            request.Headers.Add("X-Notifications-Key", key);
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task WithoutTheKey_OrWithTheWrongOne_ItIsRefused()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await CallAsync(null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await CallAsync("not-the-key")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await CallAsync(NotificationApiFactory.RunKey + "x")).StatusCode);
    }

    [Fact]
    public async Task WithTheKey_ItRunsAPass_AndSaysWhatItDid()
    {
        var admin = await factory.AdminClientAsync();
        var questionId = await CreateQuestionAsync(admin, "Capital of France?", "Rome", "Paris");
        var examId = await CreateExamAsync(admin, $"Geography {Guid.NewGuid():N}", [questionId], TimeSpan.FromHours(20));
        var (candidate, user) = await factory.EnrollNewCandidateAsync(admin, examId);
        using var _a = admin;
        using var _c = candidate;

        var response = await CallAsync(NotificationApiFactory.RunKey);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var summary = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(summary.GetProperty("mailAvailable").GetBoolean());
        Assert.True(summary.GetProperty("remindersSent").GetInt32() >= 1);
        Assert.Single(factory.Mail.Sent, m => m.To == user.Email);
    }

    [Fact]
    public void TheRoute_IsOpenToAnyCaller_BecauseItProvesTheCallerItself()
    {
        var routes = EndpointAuthorizationInspector.ListRoutes(factory.Services).Where(r => r.Pattern == Route).ToList();

        Assert.Equal("POST", Assert.Single(routes).Method);
        Assert.All(routes, r => Assert.False(r.RequiresAuthorization));
    }
}

/// <summary>Until a run key is set the route is not there: nothing can be proven, so nothing is answered.</summary>
public sealed class NotificationRunNotConfiguredTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task TheRoute_Returns404_WhateverIsPresented()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/notifications/run");
        request.Headers.Add("X-Notifications-Key", "any-key-at-all-is-no-use-without-one-configured");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
