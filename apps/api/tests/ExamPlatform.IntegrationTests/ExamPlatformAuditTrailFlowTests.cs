using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ExamPlatform.Modules.Admin.Domain;
using ExamPlatform.Modules.Admin.Infrastructure;
using ExamPlatform.Modules.Batch.Endpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// Exam, batch and invite changes leave audit entries (FR-40): what happened, to which record, and who did it,
/// without putting a candidate's e-mail address in the trail (NFR-6).
/// </summary>
public sealed class ExamPlatformAuditTrailFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<(HttpClient Client, SignedInTestUser User)> StaffAsync()
    {
        var user = await factory.SignInAsAsync("SuperAdmin");
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.AccessToken);
        return (client, user);
    }

    private async Task<List<AuditLog>> EntriesForAsync(Guid entityId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AdminDbContext>().AuditLogs
            .AsNoTracking()
            .Where(a => a.EntityId == entityId.ToString())
            .OrderBy(a => a.OccurredAtUtc)
            .ToListAsync();
    }

    [Fact]
    public async Task CreatingAndPublishingAnExam_IsAuditedWithTheStaffMemberWhoDidIt()
    {
        var (client, staff) = await StaffAsync();
        var question = await CreateQuestionAsync(client, "What is 2 + 2?", "4", "5");

        var examId = await CreateExamAsync(client, "Audited Exam", [question], startsIn: TimeSpan.FromHours(1));

        var entries = await EntriesForAsync(examId);
        var created = Assert.Single(entries, e => e.Action == "ExamAuthoring.ExamCreated");
        Assert.Equal(staff.UserId, created.ActorUserId);
        Assert.Equal("SuperAdmin", created.ActorRole);
        Assert.Equal("Exam", created.EntityType);
        Assert.Equal("Audited Exam", created.Metadata["name"]);
        Assert.False(string.IsNullOrEmpty(created.CorrelationId));
        var published = Assert.Single(entries, e => e.Action == "ExamAuthoring.ExamPublished");
        Assert.Equal(staff.UserId, published.ActorUserId);
    }

    [Fact]
    public async Task TheBatchLifecycle_IsAudited_AndNamesMembersByIdNotByAddress()
    {
        var (client, staff) = await StaffAsync();
        var create = await client.PostAsJsonAsync("/v1/batches", new CreateBatchRequest(Guid.NewGuid(), "Audited Batch", null, 5));
        var batchId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var memberEmail = UniqueEmail();
        (await client.PostAsJsonAsync($"/v1/batches/{batchId}/members", new AddBatchMemberRequest(memberEmail))).EnsureSuccessStatusCode();
        (await client.PostAsync($"/v1/batches/{batchId}/activate", null)).EnsureSuccessStatusCode();
        (await client.PostAsync($"/v1/batches/{batchId}/close", null)).EnsureSuccessStatusCode();

        var entries = await EntriesForAsync(batchId);

        Assert.Equal(
            ["Batch.Created", "Batch.MemberAdded", "Batch.Activated", "Batch.Closed"],
            entries.Select(e => e.Action).ToArray());
        Assert.All(entries, e => Assert.Equal(staff.UserId, e.ActorUserId));
        Assert.All(entries, e => Assert.DoesNotContain(memberEmail, string.Join(' ', e.Metadata.Values), StringComparison.OrdinalIgnoreCase));
        Assert.True(Guid.TryParse(entries[1].Metadata["memberId"], out _));
    }

    [Fact]
    public async Task InviteChanges_AreAudited_WithTheInviterThenTheCandidate_AndNeverTheAddress()
    {
        var (admin, staff) = await StaffAsync();
        var question = await CreateQuestionAsync(admin, "What is 2 + 2?", "4", "5");
        var examId = await CreateExamAsync(admin, "Invite Audit Exam", [question], startsIn: TimeSpan.FromHours(-1));
        var email = UniqueEmail();
        var invite = await InviteAsync(admin, examId, email);
        var inviteId = invite.GetProperty("id").GetGuid();
        var (candidate, candidateUser) = await factory.CandidateClientAsync(email);

        (await candidate.PostAsJsonAsync("/v1/invites/accept", new { code = CodeFromLink(invite.GetProperty("inviteLink").GetString()!) }))
            .EnsureSuccessStatusCode();

        var entries = await EntriesForAsync(inviteId);
        var created = Assert.Single(entries, e => e.Action == "Invite.Created");
        Assert.Equal(staff.UserId, created.ActorUserId);
        var accepted = Assert.Single(entries, e => e.Action == "Invite.Accepted");
        Assert.Equal(candidateUser.UserId, accepted.ActorUserId);
        Assert.Equal("Candidate", accepted.ActorRole);
        Assert.Equal(examId.ToString(), accepted.Metadata["examId"]);
        Assert.All(entries, e => Assert.DoesNotContain(email, string.Join(' ', e.Metadata.Values), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ARevokedInvite_IsAudited()
    {
        var (admin, staff) = await StaffAsync();
        var question = await CreateQuestionAsync(admin, "What is 2 + 2?", "4", "5");
        var examId = await CreateExamAsync(admin, "Revoke Audit Exam", [question], startsIn: TimeSpan.FromHours(-1));
        var inviteId = (await InviteAsync(admin, examId, UniqueEmail())).GetProperty("id").GetGuid();

        var response = await admin.PostAsync($"/v1/invites/{inviteId}/revoke", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var revoked = Assert.Single(await EntriesForAsync(inviteId), e => e.Action == "Invite.Revoked");
        Assert.Equal(staff.UserId, revoked.ActorUserId);
    }
}
