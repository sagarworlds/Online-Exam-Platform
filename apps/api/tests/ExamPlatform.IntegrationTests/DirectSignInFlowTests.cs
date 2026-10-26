using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ExamPlatform.Modules.Identity.Domain;
using ExamPlatform.Modules.Identity.Infrastructure;
using ExamPlatform.Modules.Invite.Domain;
using ExamPlatform.Modules.Invite.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>An <see cref="ApiFactory"/> whose direct sign-in limit is low enough to exceed in a test.</summary>
public sealed class LowDirectSignInRateLimitApiFactory : ApiFactory
{
    /// <summary>The permits each client IP gets per window.</summary>
    public const int PermitLimit = 2;

    /// <inheritdoc />
    protected override IReadOnlyDictionary<string, string?> AdditionalConfiguration =>
        new Dictionary<string, string?>(base.AdditionalConfiguration)
        {
            ["Invite:RateLimits:DirectSignIn:PermitLimit"] = PermitLimit.ToString(CultureInfo.InvariantCulture),
        };
}

/// <summary>
/// The direct sign-in link over real HTTP and a real database: staff tick the box when inviting, and the invited student opens the link,
/// is signed in without a code, is enrolled, and is told which exam to open. Every refusal must leave the single-use code unspent.
/// </summary>
public sealed class DirectSignInFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Route = "/v1/invites/direct-sign-in";

    private async Task<(HttpClient Admin, Guid ExamId)> AdminWithPublishedExamAsync(string name = "Direct Link Exam")
    {
        var admin = await factory.AdminClientAsync();
        var question = await CreateQuestionAsync(admin, "What is 2 + 2?", "4", "5");
        return (admin, await CreateExamAsync(admin, name, [question], startsIn: TimeSpan.FromHours(-1)));
    }

    private static async Task<JsonElement> DirectInviteAsync(HttpClient admin, Guid examId, string email)
    {
        var response = await admin.PostAsJsonAsync("/v1/invites", new { examId, email, directSignIn = true });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<HttpResponseMessage> OpenAsync(string? code)
    {
        using var anonymous = factory.CreateClient();
        return await anonymous.PostAsJsonAsync(Route, new { code });
    }

    private static string DirectCode(JsonElement invite) => CodeFromLink(invite.GetProperty("inviteLink").GetString()!);

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string errorCode)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(errorCode, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
    }

    private async Task<Invite> StoredInviteAsync(Guid inviteId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<InviteDbContext>().Invites.AsNoTracking()
            .Include(i => i.Codes).SingleAsync(i => i.Id == inviteId);
    }

    private async Task SuspendAsync(Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        db.Users.Single(u => u.Id == userId).Suspend(DateTime.UtcNow);
        await db.SaveChangesAsync();
    }

    // ---- making the link ---------------------------------------------------------------------------

    [Fact]
    public async Task Create_WithTheBoxTicked_ReturnsTheDirectLink_EvenThoughNoMailServerIsConfiguredAndEvenWhenOneIs()
    {
        var (admin, examId) = await AdminWithPublishedExamAsync();
        using var _ = admin;

        var invite = await DirectInviteAsync(admin, examId, UniqueEmail());

        Assert.Matches(@"/invite/direct#code=[A-Z0-9]{16}$", invite.GetProperty("inviteLink").GetString());
        Assert.True(invite.GetProperty("hasDirectLink").GetBoolean());
    }

    [Fact]
    public async Task Create_WithoutTheBox_IsTheOrdinaryInvitation_Unchanged()
    {
        var (admin, examId) = await AdminWithPublishedExamAsync();
        using var _ = admin;

        var invite = await InviteAsync(admin, examId, UniqueEmail());

        Assert.Matches(@"/invite\?code=[A-Z0-9]{8}$", invite.GetProperty("inviteLink").GetString());
        Assert.False(invite.GetProperty("hasDirectLink").GetBoolean());
    }

    [Fact]
    public async Task Create_WithTheBoxTicked_NeedsThePermission_ARoleThatOnlyManagesInvitesCannotTickIt()
    {
        var (admin, examId) = await AdminWithPublishedExamAsync();
        using var _ = admin;
        var examAdmin = await factory.SignInAsAsync("ExamAdmin");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", examAdmin.AccessToken);

        // The same role can still make an ordinary invitation: invite.manage is not what is missing.
        var ordinary = await client.PostAsJsonAsync("/v1/invites", new { examId, email = UniqueEmail() });
        var refused = await client.PostAsJsonAsync("/v1/invites", new { examId, email = UniqueEmail(), directSignIn = true });

        Assert.Equal(HttpStatusCode.Created, ordinary.StatusCode);
        await AssertProblemAsync(refused, HttpStatusCode.Forbidden, "direct_link_not_permitted");
    }

    [Fact]
    public async Task GenerateCode_ForADirectLink_NeedsThePermission_AndMakesAnotherDirectCode()
    {
        var (admin, examId) = await AdminWithPublishedExamAsync();
        using var _ = admin;
        var invite = await InviteAsync(admin, examId, UniqueEmail());
        var inviteId = invite.GetProperty("id").GetGuid();
        var examAdmin = await factory.SignInAsAsync("ExamAdmin");
        using var limited = factory.CreateClient();
        limited.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", examAdmin.AccessToken);

        await AssertProblemAsync(
            await limited.PostAsJsonAsync($"/v1/invites/{inviteId}/codes", new { directSignIn = true }),
            HttpStatusCode.Forbidden, "direct_link_not_permitted");

        var made = await admin.PostAsJsonAsync($"/v1/invites/{inviteId}/codes", new { directSignIn = true });

        Assert.Equal(HttpStatusCode.Created, made.StatusCode);
        var code = await made.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(code.GetProperty("directSignIn").GetBoolean());
        Assert.Matches(@"/invite/direct#code=[A-Z0-9]{16}$", code.GetProperty("link").GetString());
        Assert.Equal(HttpStatusCode.OK, (await OpenAsync(code.GetProperty("code").GetString())).StatusCode);
    }

    [Fact]
    public async Task TheInviteList_SaysWhichInvitationsHaveADirectLink()
    {
        var (admin, examId) = await AdminWithPublishedExamAsync();
        using var _ = admin;
        var direct = await DirectInviteAsync(admin, examId, UniqueEmail());
        var ordinary = await InviteAsync(admin, examId, UniqueEmail());

        var list = (await admin.GetFromJsonAsync<JsonElement>("/v1/invites")).EnumerateArray().ToList();

        bool HasDirect(JsonElement invite) =>
            list.Single(i => i.GetProperty("id").GetGuid() == invite.GetProperty("id").GetGuid()).GetProperty("hasDirectLink").GetBoolean();
        Assert.True(HasDirect(direct));
        Assert.False(HasDirect(ordinary));
    }

    // ---- opening it ---------------------------------------------------------------------------------

    [Fact]
    public async Task ANewStudent_OpensTheLink_IsSignedInInTheirNewAccount_EnrolledAndToldWhichExamToOpen()
    {
        var (admin, examId) = await AdminWithPublishedExamAsync("Direct Maths");
        using var _ = admin;
        var email = UniqueEmail();
        var invite = await DirectInviteAsync(admin, examId, email);

        var response = await OpenAsync(DirectCode(invite));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(examId, body.GetProperty("examId").GetGuid());
        var token = body.GetProperty("accessToken").GetString()!;

        // The token is a real session: it lists the exam the invitation was for.
        using var student = factory.CreateClient();
        student.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var exam = Assert.Single((await student.GetFromJsonAsync<JsonElement>("/v1/me/exams")).EnumerateArray());
        Assert.Equal(examId, exam.GetProperty("examId").GetGuid());

        using var scope = factory.Services.CreateScope();
        var made = await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Users.AsNoTracking()
            .Include(u => u.Roles).SingleAsync(u => u.Email == email);
        Assert.Equal(UserStatus.Active, made.Status);
        Assert.True(made.IsStudent);
        Assert.Equal(User.PlaceholderAdultDateOfBirth, made.DateOfBirth);
        var stored = await StoredInviteAsync(invite.GetProperty("id").GetGuid());
        Assert.Equal(InviteStatus.Accepted, stored.Status);
        Assert.Equal(made.Id, stored.AcceptedByUserId);
    }

    [Fact]
    public async Task AnExistingStudent_OpensTheLink_IsSignedInAsThem_WithoutAnotherAccount()
    {
        var (admin, examId) = await AdminWithPublishedExamAsync();
        using var _ = admin;
        var email = UniqueEmail();
        var existing = await factory.SignInAsAsync("Candidate", email);
        var invite = await DirectInviteAsync(admin, examId, email);

        var response = await OpenAsync(DirectCode(invite));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.Equal(1, await db.Users.CountAsync(u => u.Email == email));
        var stored = await StoredInviteAsync(invite.GetProperty("id").GetGuid());
        Assert.Equal(existing.UserId, stored.AcceptedByUserId);
    }

    [Fact]
    public async Task TheAddressIsMatchedWithoutRegardToCase_SoAStudentRegisteredWithACapitalIsNotMadeASecondAccount()
    {
        var (admin, examId) = await AdminWithPublishedExamAsync();
        using var _ = admin;
        var local = $"mixed-{Guid.NewGuid():N}";
        var existing = await factory.SignInAsAsync("Candidate", $"{local}@Tests.Local");
        var invite = await DirectInviteAsync(admin, examId, $"{local}@tests.local");

        Assert.Equal(HttpStatusCode.OK, (await OpenAsync(DirectCode(invite))).StatusCode);

        var stored = await StoredInviteAsync(invite.GetProperty("id").GetGuid());
        Assert.Equal(existing.UserId, stored.AcceptedByUserId);
    }

    [Fact]
    public async Task OpeningTheLink_IsRecordedInTheAuditTrail_AsTheStudent_WithoutTheCodeOrTheAddress()
    {
        var (admin, examId) = await AdminWithPublishedExamAsync();
        using var _ = admin;
        var email = UniqueEmail();
        var invite = await DirectInviteAsync(admin, examId, email);
        var code = DirectCode(invite);
        var inviteId = invite.GetProperty("id").GetGuid();

        var body = await (await OpenAsync(code)).Content.ReadFromJsonAsync<JsonElement>();

        var studentId = (await StoredInviteAsync(inviteId)).AcceptedByUserId!.Value;
        var entries = (await admin.GetFromJsonAsync<JsonElement>($"/v1/admin/audit-logs?entityType=Invite&actorUserId={studentId}&pageSize=100"))
            .EnumerateArray().ToList();
        var used = Assert.Single(entries, e => e.GetProperty("action").GetString() == "Invite.DirectLinkUsed");
        Assert.Equal(inviteId.ToString(), used.GetProperty("entityId").GetString());
        var everything = used.GetRawText();
        Assert.DoesNotContain(code, everything, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(email, everything, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrEmpty(body.GetProperty("accessToken").GetString()));
    }

    // ---- refusals -------------------------------------------------------------------------------------

    [Fact]
    public async Task OpeningTheLinkASecondTime_IsRefused_BecauseTheCodeWorksOnce()
    {
        var (admin, examId) = await AdminWithPublishedExamAsync();
        using var _ = admin;
        var code = DirectCode(await DirectInviteAsync(admin, examId, UniqueEmail()));

        Assert.Equal(HttpStatusCode.OK, (await OpenAsync(code)).StatusCode);

        await AssertProblemAsync(await OpenAsync(code), HttpStatusCode.BadRequest, "invalid_invite_code");
    }

    [Fact]
    public async Task AnOrdinaryCode_CannotSignAnyoneIn_AndIsLeftUsableForTheOrdinaryWay()
    {
        var (admin, examId) = await AdminWithPublishedExamAsync();
        using var _ = admin;
        var email = UniqueEmail();
        var (candidate, _) = await factory.CandidateClientAsync(email);
        var ordinary = await InviteAsync(admin, examId, email);
        var code = CodeFromLink(ordinary.GetProperty("inviteLink").GetString()!);

        await AssertProblemAsync(await OpenAsync(code), HttpStatusCode.BadRequest, "invalid_invite_code");

        // Still good for the signed-in way: the refusal did not spend it.
        Assert.Equal(HttpStatusCode.OK, (await candidate.PostAsJsonAsync("/v1/invites/accept", new { code })).StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("NOSUCHCODE0000000")]
    public async Task AMissingOrUnknownCode_IsTheSameAnswerAsASpentOne(string? code) =>
        await AssertProblemAsync(await OpenAsync(code), HttpStatusCode.BadRequest, "invalid_invite_code");

    [Theory]
    [InlineData("ExamAdmin")]
    [InlineData("InstituteTeacher")]
    [InlineData("SuperAdmin")]
    public async Task AStaffAccount_IsNeverSignedInByALink_AndTheCodeIsLeftUnspent(string role)
    {
        var (admin, examId) = await AdminWithPublishedExamAsync();
        using var _ = admin;
        var email = UniqueEmail();
        await factory.SignInAsAsync(role, email);
        var invite = await DirectInviteAsync(admin, examId, email);

        await AssertProblemAsync(await OpenAsync(DirectCode(invite)), HttpStatusCode.Forbidden, "direct_sign_in_not_allowed");

        var stored = await StoredInviteAsync(invite.GetProperty("id").GetGuid());
        Assert.Equal(InviteStatus.Pending, stored.Status);
        Assert.Null(stored.Codes.Single().UsedAt);
    }

    [Fact]
    public async Task ALockedAccount_IsRefused_AndTheCodeIsLeftUnspent()
    {
        var (admin, examId) = await AdminWithPublishedExamAsync();
        using var _ = admin;
        var email = UniqueEmail();
        var student = await factory.SignInAsAsync("Candidate", email);
        await SuspendAsync(student.UserId);
        var invite = await DirectInviteAsync(admin, examId, email);

        await AssertProblemAsync(await OpenAsync(DirectCode(invite)), HttpStatusCode.Forbidden, "account_locked");

        Assert.Equal(InviteStatus.Pending, (await StoredInviteAsync(invite.GetProperty("id").GetGuid())).Status);
    }

    [Fact]
    public async Task ChangingTheInvitedAddress_StopsALinkSentToTheOldOne()
    {
        var (admin, examId) = await AdminWithPublishedExamAsync();
        using var _ = admin;
        var oldEmail = UniqueEmail();
        var code = DirectCode(await DirectInviteAsync(admin, examId, oldEmail));

        (await admin.PostAsJsonAsync("/v1/invites/change-email", new { currentEmail = oldEmail, newEmail = UniqueEmail() })).EnsureSuccessStatusCode();

        await AssertProblemAsync(await OpenAsync(code), HttpStatusCode.BadRequest, "invalid_invite_code");
    }

    [Fact]
    public async Task ARevokedInvitation_CannotBeOpened()
    {
        var (admin, examId) = await AdminWithPublishedExamAsync();
        using var _ = admin;
        var invite = await DirectInviteAsync(admin, examId, UniqueEmail());
        (await admin.PostAsync($"/v1/invites/{invite.GetProperty("id").GetGuid()}/revoke", content: null)).EnsureSuccessStatusCode();

        await AssertProblemAsync(await OpenAsync(DirectCode(invite)), HttpStatusCode.BadRequest, "invalid_invite_code");
    }

    [Fact]
    public async Task TwoClicksAtOnce_HaveOneWinner_SoTheLinkSignsInOnce()
    {
        var (admin, examId) = await AdminWithPublishedExamAsync();
        using var _ = admin;
        var email = UniqueEmail();
        await factory.SignInAsAsync("Candidate", email);
        var code = DirectCode(await DirectInviteAsync(admin, examId, email));

        var responses = await Task.WhenAll(OpenAsync(code), OpenAsync(code), OpenAsync(code));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.All(
            responses.Where(r => r.StatusCode != HttpStatusCode.OK),
            r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.Conflict }));
    }

    // ---- the switch -------------------------------------------------------------------------------------

    [Fact]
    public async Task WhenSwitchedOff_NoLinkIsMade_AndNoLinkAlreadySentWorks_ButOrdinaryInvitationsStillDo()
    {
        var (admin, examId) = await AdminWithPublishedExamAsync();
        using var _ = admin;
        var code = DirectCode(await DirectInviteAsync(admin, examId, UniqueEmail()));
        await using var off = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["Invite:DirectSignIn:Enabled"] = "false" })));
        using var client = off.CreateClient();
        client.DefaultRequestHeaders.Authorization = admin.DefaultRequestHeaders.Authorization;

        await AssertProblemAsync(
            await client.PostAsJsonAsync("/v1/invites", new { examId, email = UniqueEmail(), directSignIn = true }),
            HttpStatusCode.Forbidden, "direct_sign_in_disabled");
        await AssertProblemAsync(await client.PostAsJsonAsync(Route, new { code }), HttpStatusCode.Forbidden, "direct_sign_in_disabled");
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/v1/invites", new { examId, email = UniqueEmail() })).StatusCode);
    }
}

/// <summary>The direct sign-in route is behind its own per-client limit (NFR-5), and a rejection is the same structured 429.</summary>
public sealed class DirectSignInRateLimitTests(LowDirectSignInRateLimitApiFactory factory) : IClassFixture<LowDirectSignInRateLimitApiFactory>
{
    [Fact]
    public async Task BeyondTheLimit_TheRouteAnswers429_WhateverTheCodeIs()
    {
        using var client = factory.CreateClient();

        var answers = new List<HttpStatusCode>();
        for (var i = 0; i <= LowDirectSignInRateLimitApiFactory.PermitLimit; i++)
        {
            answers.Add((await client.PostAsJsonAsync("/v1/invites/direct-sign-in", new { code = "GUESS" + i })).StatusCode);
        }

        Assert.All(answers.Take(LowDirectSignInRateLimitApiFactory.PermitLimit), status => Assert.Equal(HttpStatusCode.BadRequest, status));
        Assert.Equal(HttpStatusCode.TooManyRequests, answers[^1]);
    }
}
