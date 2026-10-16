using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ExamPlatform.Modules.Admin.Infrastructure;
using ExamPlatform.Modules.Identity.Application.Commands;
using ExamPlatform.Modules.Invite.Application.Commands;
using ExamPlatform.SharedKernel.Application.LoadTest;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// The load test's candidate pool (ADR 0006), from nothing to a pool account starting an attempt the way the load scripts do: sign in
/// with the shared password (no second factor), then start the exam it was enrolled in. The accounts are made by the real handlers
/// against the real database, so this is what the staging run will rely on.
/// </summary>
public sealed class LoadTestPoolFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "a long shared load test passphrase 2026";

    [Fact]
    public async Task APoolAccount_SignsInWithTheSharedPassword_AndStartsTheExamItWasEnrolledIn_AndNoOtherExam()
    {
        var pool = NewPool(count: 3);
        using var admin = await factory.AdminClientAsync();
        var questionId = await CreateQuestionAsync(admin, "What is 2 + 2?", "4", "5");
        var loadTestExam = await CreateExamAsync(admin, "Load test exam", [questionId], startsIn: TimeSpan.FromHours(-1));
        var otherExam = await CreateExamAsync(admin, "Some other exam", [questionId], startsIn: TimeSpan.FromHours(-1));

        var created = await ProvisionAsync(pool);
        var enrolled = await EnrolAsync(loadTestExam, pool);

        Assert.Equal(new ProvisionLoadTestCandidatesResult(3, 0), created);
        Assert.Equal(new EnrolLoadTestPoolResult(3, 0, 0), enrolled);

        using var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/v1/auth/login", new { email = pool.AddressOf(2), password = Password });
        login.EnsureSuccessStatusCode();
        var auth = await login.Content.ReadFromJsonAsync<JsonElement>();
        // The Candidate role needs no second factor, so the password alone signs in, as the load scripts expect.
        Assert.False(auth.GetProperty("requiresTwoFactor").GetBoolean());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("accessToken").GetString());

        var start = await client.PostAsJsonAsync($"/v1/me/exams/{loadTestExam}/attempts", new { instructionsAcknowledged = true });
        start.EnsureSuccessStatusCode();
        Assert.NotEqual(Guid.Empty, (await start.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());

        var elsewhere = await client.PostAsJsonAsync($"/v1/me/exams/{otherExam}/attempts", new { instructionsAcknowledged = true });
        Assert.False(elsewhere.IsSuccessStatusCode, "A pool account is enrolled in the load-test exam only.");
    }

    [Fact]
    public async Task RunningBothCommandsAgain_ChangesNothing()
    {
        var pool = NewPool(count: 3);
        using var admin = await factory.AdminClientAsync();
        var examId = await CreateExamAsync(admin, "Load test exam", [await CreateQuestionAsync(admin, "What is 1 + 1?", "2", "3")], TimeSpan.FromHours(-1));
        await ProvisionAsync(pool);
        await EnrolAsync(examId, pool);

        var created = await ProvisionAsync(pool);
        var enrolled = await EnrolAsync(examId, pool);

        Assert.Equal(new ProvisionLoadTestCandidatesResult(0, 3), created);
        Assert.Equal(new EnrolLoadTestPoolResult(0, 3, 0), enrolled);
    }

    [Fact]
    public async Task EnrolBeforeTheAccountsExist_EnrolsNobody_AndSaysHowManyAddressesHadNoAccount()
    {
        var pool = NewPool(count: 2);
        using var admin = await factory.AdminClientAsync();
        var examId = await CreateExamAsync(admin, "Load test exam", [await CreateQuestionAsync(admin, "What is 3 + 3?", "6", "7")], TimeSpan.FromHours(-1));

        var enrolled = await EnrolAsync(examId, pool);

        Assert.Equal(new EnrolLoadTestPoolResult(0, 0, 2), enrolled);
    }

    [Fact]
    public async Task TheRuns_AreRecordedOnceEach_WithCounts_AndNoPoolAddress()
    {
        var pool = NewPool(count: 2);
        using var admin = await factory.AdminClientAsync();
        var examId = await CreateExamAsync(admin, "Audited load test exam", [await CreateQuestionAsync(admin, "What is 4 + 4?", "8", "9")], TimeSpan.FromHours(-1));
        await ProvisionAsync(pool);
        await EnrolAsync(examId, pool);

        using var scope = factory.Services.CreateScope();
        var logs = await scope.ServiceProvider.GetRequiredService<AdminDbContext>().AuditLogs.AsNoTracking()
            .Where(a => a.Action == "Invite.LoadTestCandidatesEnrolled" && a.EntityId == examId.ToString())
            .ToListAsync();

        var entry = Assert.Single(logs);
        Assert.Equal("2", entry.Metadata["enrolled"]);
        Assert.DoesNotContain(".invalid", string.Join(' ', entry.Metadata.Values));
        Assert.Null(entry.ActorUserId);
    }

    // A pool of its own for each test, so tests in this class never meet each other's accounts.
    private static LoadTestAccountPattern NewPool(int count)
    {
        Assert.True(LoadTestAccountPattern.TryCreate($"pool-{Guid.NewGuid():N}-{{n}}@load.invalid", count, out var problem, out var pattern), problem);
        return pattern;
    }

    private async Task<ProvisionLoadTestCandidatesResult> ProvisionAsync(LoadTestAccountPattern pool)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ProvisionLoadTestCandidatesHandler>()
            .HandleAsync(new ProvisionLoadTestCandidatesCommand(pool, Password), CancellationToken.None);
    }

    private async Task<EnrolLoadTestPoolResult> EnrolAsync(Guid examId, LoadTestAccountPattern pool)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<EnrolLoadTestCandidatesHandler>()
            .HandleAsync(new EnrolLoadTestPoolCommand(examId, pool), CancellationToken.None);
    }
}
