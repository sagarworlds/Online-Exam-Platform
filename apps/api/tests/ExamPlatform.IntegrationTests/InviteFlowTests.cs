using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExamPlatform.Modules.Invite.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// Drives invitations over real HTTP and a real database (FR-14, FR-50a): staff invite an address to an exam, the
/// invited candidate accepts with the code from the link, and accepting enrolls them in the exam.
/// </summary>
public sealed class InviteFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<(HttpClient Admin, Guid ExamId)> AdminWithPublishedExamAsync(string name = "Invite Test Exam")
    {
        var admin = await factory.AdminClientAsync();
        var question = await CreateQuestionAsync(admin, "What is 2 + 2?", "4", "5");
        var examId = await CreateExamAsync(admin, name, [question], startsIn: TimeSpan.FromHours(-1));
        return (admin, examId);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string errorCode)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(errorCode, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
    }

    // ---- creating an invitation -------------------------------------------------------------------

    [Fact]
    public async Task Create_ForAnExam_WithNoMailServer_ReturnsTheLinkToPassOn()
    {
        var (admin, examId) = await AdminWithPublishedExamAsync("Maths Final");
        var email = UniqueEmail();

        var response = await admin.PostAsJsonAsync("/v1/invites", new { examId, email });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var invite = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(email, invite.GetProperty("email").GetString());
        Assert.Equal("Maths Final", invite.GetProperty("examName").GetString());
        Assert.Equal("Pending", invite.GetProperty("status").GetString());
        Assert.False(invite.GetProperty("emailSent").GetBoolean());
        var link = invite.GetProperty("inviteLink").GetString()!;
        Assert.Matches(@"/invite\?code=[A-Z0-9]{8}$", link);

        // The code the link carries comes back with it, so the inviter can copy either one to hand over.
        Assert.Equal(CodeFromLink(link), invite.GetProperty("inviteCode").GetString());
    }

    [Fact]
    public async Task Create_RecordsTheCallerAsInviter_NotAValueFromTheBody()
    {
        var (admin, examId) = await AdminWithPublishedExamAsync();
        var spoofed = Guid.NewGuid();

        var response = await admin.PostAsJsonAsync("/v1/invites", new { examId, email = UniqueEmail(), createdByUserId = spoofed });

        var inviteId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var scope = factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<InviteDbContext>().Invites.AsNoTracking().SingleAsync(i => i.Id == inviteId);
        Assert.NotEqual(spoofed, stored.CreatedByUserId);
        Assert.NotEqual(Guid.Empty, stored.CreatedByUserId);
    }

    [Fact]
    public async Task Create_ForAnExamThatDoesNotExist_Returns404()
    {
        using var admin = await factory.AdminClientAsync();

        var response = await admin.PostAsJsonAsync("/v1/invites", new { examId = Guid.NewGuid(), email = UniqueEmail() });

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "exam_not_found");
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("")]
    public async Task Create_WithAnInvalidEmail_Returns400(string email)
    {
        var (admin, examId) = await AdminWithPublishedExamAsync();

        var response = await admin.PostAsJsonAsync("/v1/invites", new { examId, email });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "invalid_invite_email");
    }

    [Fact]
    public async Task Create_WithoutAuth_Returns401()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/v1/invites", new { examId = Guid.NewGuid(), email = "x@example.com" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task List_ShowsTheInvitesStaffCreated()
    {
        var (admin, examId) = await AdminWithPublishedExamAsync();
        var email = UniqueEmail();
        await InviteAsync(admin, examId, email);

        var list = await admin.GetFromJsonAsync<JsonElement>("/v1/invites");

        Assert.Contains(list.EnumerateArray(), i => i.GetProperty("email").GetString() == email);
    }

    // ---- accepting --------------------------------------------------------------------------------

    [Fact]
    public async Task Accept_AsTheInvitedCandidate_EnrollsThem()
    {
        var (admin, examId) = await AdminWithPublishedExamAsync();
        var email = UniqueEmail();
        var code = CodeFromLink((await InviteAsync(admin, examId, email)).GetProperty("inviteLink").GetString()!);
        var (candidate, user) = await factory.CandidateClientAsync(email);

        var response = await candidate.PostAsJsonAsync("/v1/invites/accept", new { code });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var accepted = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Accepted", accepted.GetProperty("status").GetString());
        Assert.Equal(examId, accepted.GetProperty("examId").GetGuid());

        using var scope = factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<InviteDbContext>().Invites
            .AsNoTracking().Include(i => i.Codes).SingleAsync(i => i.Id == accepted.GetProperty("id").GetGuid());
        Assert.Equal(user.UserId, stored.AcceptedByUserId);
        Assert.NotNull(stored.Codes.Single().UsedAt);
    }

    [Fact]
    public async Task Accept_TheCodeInLowerCase_Works()
    {
        var (admin, examId) = await AdminWithPublishedExamAsync();
        var email = UniqueEmail();
        var code = CodeFromLink((await InviteAsync(admin, examId, email)).GetProperty("inviteLink").GetString()!);
        var (candidate, _) = await factory.CandidateClientAsync(email);

        var response = await candidate.PostAsJsonAsync("/v1/invites/accept", new { code = code.ToLowerInvariant() });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Accept_TheSameCodeTwiceAtOnce_LetsExactlyOneThrough()
    {
        var (admin, examId) = await AdminWithPublishedExamAsync();
        var email = UniqueEmail();
        var code = CodeFromLink((await InviteAsync(admin, examId, email)).GetProperty("inviteLink").GetString()!);
        var (candidate, _) = await factory.CandidateClientAsync(email);

        var responses = await Task.WhenAll(
            candidate.PostAsJsonAsync("/v1/invites/accept", new { code }),
            candidate.PostAsJsonAsync("/v1/invites/accept", new { code }));

        // Both can read the invite as pending; the row version makes the second save lose with a typed
        // 409 (or, when it reads after the first has committed, the ordinary "code already used" 400).
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        var loser = responses.Single(r => r.StatusCode != HttpStatusCode.OK);
        Assert.True(
            loser.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.BadRequest,
            $"The losing accept answered {(int)loser.StatusCode}.");
    }

    [Fact]
    public async Task Accept_TheSameCodeTwice_RefusesTheSecond()
    {
        var (admin, examId) = await AdminWithPublishedExamAsync();
        var email = UniqueEmail();
        var code = CodeFromLink((await InviteAsync(admin, examId, email)).GetProperty("inviteLink").GetString()!);
        var (candidate, _) = await factory.CandidateClientAsync(email);
        (await candidate.PostAsJsonAsync("/v1/invites/accept", new { code })).EnsureSuccessStatusCode();

        var again = await candidate.PostAsJsonAsync("/v1/invites/accept", new { code });

        await AssertProblemAsync(again, HttpStatusCode.BadRequest, "invalid_invite_code");
    }

    [Fact]
    public async Task Accept_ByAnAccountWithAnotherAddress_Returns403_AndTheInvitedOneCanStillAccept()
    {
        var (admin, examId) = await AdminWithPublishedExamAsync();
        var invitedEmail = UniqueEmail();
        var code = CodeFromLink((await InviteAsync(admin, examId, invitedEmail)).GetProperty("inviteLink").GetString()!);
        var (stranger, _) = await factory.CandidateClientAsync(UniqueEmail());
        var (invited, _) = await factory.CandidateClientAsync(invitedEmail);

        await AssertProblemAsync(
            await stranger.PostAsJsonAsync("/v1/invites/accept", new { code }), HttpStatusCode.Forbidden, "invite_email_mismatch");

        // The stranger's attempt did not burn the code.
        Assert.Equal(HttpStatusCode.OK, (await invited.PostAsJsonAsync("/v1/invites/accept", new { code })).StatusCode);
    }

    [Theory]
    [InlineData("NOSUCHCD")]
    [InlineData("")]
    public async Task Accept_WithAnUnknownOrBlankCode_Returns400(string code)
    {
        var (candidate, _) = await factory.CandidateClientAsync(UniqueEmail());

        await AssertProblemAsync(
            await candidate.PostAsJsonAsync("/v1/invites/accept", new { code }), HttpStatusCode.BadRequest, "invalid_invite_code");
    }

    [Fact]
    public async Task Accept_WithoutABody_Returns400NotACrash()
    {
        var (candidate, _) = await factory.CandidateClientAsync(UniqueEmail());

        var response = await candidate.PostAsJsonAsync("/v1/invites/accept", new { });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "invalid_invite_code");
    }

    [Fact]
    public async Task Accept_WithoutAuth_Returns401()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/v1/invites/accept", new { code = "ABCDEFGH" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Accept_ARevokedInvite_Returns400()
    {
        var (admin, examId) = await AdminWithPublishedExamAsync();
        var email = UniqueEmail();
        var invite = await InviteAsync(admin, examId, email);
        var code = CodeFromLink(invite.GetProperty("inviteLink").GetString()!);
        (await admin.PostAsync($"/v1/invites/{invite.GetProperty("id").GetGuid()}/revoke", null)).EnsureSuccessStatusCode();
        var (candidate, _) = await factory.CandidateClientAsync(email);

        await AssertProblemAsync(
            await candidate.PostAsJsonAsync("/v1/invites/accept", new { code }), HttpStatusCode.BadRequest, "invalid_invite_code");
    }

    [Fact]
    public async Task Accept_ADeclinedInvite_Returns409()
    {
        var (admin, examId) = await AdminWithPublishedExamAsync();
        var email = UniqueEmail();
        var invite = await InviteAsync(admin, examId, email);
        var code = CodeFromLink(invite.GetProperty("inviteLink").GetString()!);
        (await admin.PostAsync($"/v1/invites/{invite.GetProperty("id").GetGuid()}/decline", null)).EnsureSuccessStatusCode();
        var (candidate, _) = await factory.CandidateClientAsync(email);

        await AssertProblemAsync(
            await candidate.PostAsJsonAsync("/v1/invites/accept", new { code }), HttpStatusCode.Conflict, "invite_state_invalid");
    }

    // ---- staff actions on an invite ---------------------------------------------------------------

    [Fact]
    public async Task Revoke_Twice_Returns409_AndAnUnknownInvite_Returns404()
    {
        var (admin, examId) = await AdminWithPublishedExamAsync();
        var inviteId = (await InviteAsync(admin, examId, UniqueEmail())).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/v1/invites/{inviteId}/revoke", null)).StatusCode);
        await AssertProblemAsync(await admin.PostAsync($"/v1/invites/{inviteId}/revoke", null), HttpStatusCode.Conflict, "invite_state_invalid");
        await AssertProblemAsync(await admin.PostAsync($"/v1/invites/{Guid.NewGuid()}/revoke", null), HttpStatusCode.NotFound, "invite_not_found");
    }

    [Fact]
    public async Task GenerateCode_AddsAWorkingCode_AndRefusesABadLifetime()
    {
        var (admin, examId) = await AdminWithPublishedExamAsync();
        var email = UniqueEmail();
        var inviteId = (await InviteAsync(admin, examId, email)).GetProperty("id").GetGuid();

        var created = await admin.PostAsJsonAsync($"/v1/invites/{inviteId}/codes", new { expiryHours = 24 });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        var code = body.GetProperty("code").GetString();

        // The link that carries the code comes with it, for handing over as a link rather than a code.
        Assert.Matches(@"/invite\?code=[A-Z0-9]{8}$", body.GetProperty("link").GetString());
        Assert.Equal(code, CodeFromLink(body.GetProperty("link").GetString()!));
        var (candidate, _) = await factory.CandidateClientAsync(email);
        Assert.Equal(HttpStatusCode.OK, (await candidate.PostAsJsonAsync("/v1/invites/accept", new { code })).StatusCode);

        await AssertProblemAsync(
            await admin.PostAsJsonAsync($"/v1/invites/{inviteId}/codes", new { expiryHours = 0 }), HttpStatusCode.BadRequest, "invalid_invite_expiry");
        await AssertProblemAsync(
            await admin.PostAsJsonAsync($"/v1/invites/{Guid.NewGuid()}/codes", new { expiryHours = 24 }), HttpStatusCode.NotFound, "invite_not_found");
    }

    [Fact]
    public async Task GenerateCode_GivesAFreshCodeEachTime_AndEveryOneWorksForTheInvitedCandidate()
    {
        var (admin, examId) = await AdminWithPublishedExamAsync();
        var email = UniqueEmail();
        var invite = await InviteAsync(admin, examId, email);
        var inviteId = invite.GetProperty("id").GetGuid();

        var first = await (await admin.PostAsJsonAsync($"/v1/invites/{inviteId}/codes", new { })).Content.ReadFromJsonAsync<JsonElement>();
        var second = await (await admin.PostAsJsonAsync($"/v1/invites/{inviteId}/codes", new { })).Content.ReadFromJsonAsync<JsonElement>();

        var codes = new[] { first.GetProperty("code").GetString(), second.GetProperty("code").GetString(), invite.GetProperty("inviteCode").GetString() };
        Assert.Equal(3, codes.Distinct().Count());

        // Any of them is the invitation's key: the candidate enters one, and the others stop mattering.
        var (candidate, _) = await factory.CandidateClientAsync(email);
        Assert.Equal(HttpStatusCode.OK, (await candidate.PostAsJsonAsync("/v1/invites/accept", new { code = codes[1] })).StatusCode);
        await AssertProblemAsync(
            await candidate.PostAsJsonAsync("/v1/invites/accept", new { code = codes[0] }), HttpStatusCode.Conflict, "invite_state_invalid");
    }

    [Fact]
    public async Task GenerateCode_ForAnInviteNoLongerPending_Returns409_AndAddsNoCode()
    {
        var (admin, examId) = await AdminWithPublishedExamAsync();
        var email = UniqueEmail();
        var revoked = (await InviteAsync(admin, examId, email)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/v1/invites/{revoked}/revoke", null)).StatusCode);

        await AssertProblemAsync(
            await admin.PostAsJsonAsync($"/v1/invites/{revoked}/codes", new { expiryHours = 24 }), HttpStatusCode.Conflict, "invite_state_invalid");

        var accepted = await InviteAsync(admin, examId, UniqueEmail());
        var acceptedEmail = accepted.GetProperty("email").GetString()!;
        var (candidate, _) = await factory.CandidateClientAsync(acceptedEmail);
        (await candidate.PostAsJsonAsync("/v1/invites/accept", new { code = accepted.GetProperty("inviteCode").GetString() })).EnsureSuccessStatusCode();

        await AssertProblemAsync(
            await admin.PostAsJsonAsync($"/v1/invites/{accepted.GetProperty("id").GetGuid()}/codes", new { expiryHours = 24 }),
            HttpStatusCode.Conflict,
            "invite_state_invalid");
    }

    // ---- what enrollment gives the candidate ------------------------------------------------------

    [Fact]
    public async Task MyExams_ListsAnAcceptedPublishedExam_AndNothingBeforeAccepting()
    {
        var (admin, examId) = await AdminWithPublishedExamAsync("Enrolled Exam");
        var email = UniqueEmail();
        var invite = await InviteAsync(admin, examId, email);
        var (candidate, _) = await factory.CandidateClientAsync(email);

        var before = await candidate.GetFromJsonAsync<JsonElement>("/v1/me/exams");
        Assert.Empty(before.EnumerateArray());

        (await candidate.PostAsJsonAsync("/v1/invites/accept", new { code = CodeFromLink(invite.GetProperty("inviteLink").GetString()!) })).EnsureSuccessStatusCode();

        var after = await candidate.GetFromJsonAsync<JsonElement>("/v1/me/exams");
        var exam = Assert.Single(after.EnumerateArray());
        Assert.Equal(examId, exam.GetProperty("examId").GetGuid());
        Assert.Equal("Enrolled Exam", exam.GetProperty("name").GetString());
        Assert.Equal(1, exam.GetProperty("questionCount").GetInt32());
        Assert.Equal(3600, exam.GetProperty("durationSeconds").GetInt32());
        Assert.Equal("Open", exam.GetProperty("state").GetString());
    }

    [Fact]
    public async Task MyExams_ShowsAFutureExamAsNotOpen_AndHidesAnUnpublishedOne()
    {
        using var admin = await factory.AdminClientAsync();
        var question = await CreateQuestionAsync(admin, "Q?", "A", "B");
        var future = await CreateExamAsync(admin, "Future Exam", [question], startsIn: TimeSpan.FromDays(2));
        var draft = await CreateExamAsync(admin, "Draft Exam", [question], startsIn: TimeSpan.FromHours(-1), publish: false);
        var email = UniqueEmail();
        var (candidate, _) = await factory.CandidateClientAsync(email);
        foreach (var examId in new[] { future, draft })
        {
            var invite = await InviteAsync(admin, examId, email);
            (await candidate.PostAsJsonAsync("/v1/invites/accept", new { code = CodeFromLink(invite.GetProperty("inviteLink").GetString()!) })).EnsureSuccessStatusCode();
        }

        var list = (await candidate.GetFromJsonAsync<JsonElement>("/v1/me/exams")).EnumerateArray().ToList();

        var only = Assert.Single(list);
        Assert.Equal("Future Exam", only.GetProperty("name").GetString());
        Assert.Equal("NotOpen", only.GetProperty("state").GetString());
    }

    [Fact]
    public async Task MyExams_NeverShowsAnotherCandidatesExams()
    {
        var (admin, examId) = await AdminWithPublishedExamAsync();
        var email = UniqueEmail();
        var invite = await InviteAsync(admin, examId, email);
        var (invited, _) = await factory.CandidateClientAsync(email);
        (await invited.PostAsJsonAsync("/v1/invites/accept", new { code = CodeFromLink(invite.GetProperty("inviteLink").GetString()!) })).EnsureSuccessStatusCode();
        var (other, _) = await factory.CandidateClientAsync(UniqueEmail());

        var list = await other.GetFromJsonAsync<JsonElement>("/v1/me/exams");

        Assert.Empty(list.EnumerateArray());
    }

    [Fact]
    public async Task MyExams_WithoutAuth_Returns401()
    {
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/v1/me/exams")).StatusCode);
    }
}
