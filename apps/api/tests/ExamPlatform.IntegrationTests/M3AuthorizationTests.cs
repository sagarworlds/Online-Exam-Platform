using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using ExamPlatform.Modules.Batch.Infrastructure;
using ExamPlatform.Modules.Identity.Domain.Rbac;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// Proves the exam, batch, invite and guardian routes check what the caller may do and not just that
/// the caller is signed in (FR-2, NFR-5). Callers are real signed-in users holding real seeded roles,
/// so a missing or mistyped permission policy cannot hide behind a hand-made token.
/// </summary>
public sealed partial class M3AuthorizationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    /// <summary>A route that needs a permission, with a body that is shaped right so a permitted call reaches the handler.</summary>
    private sealed record StaffRoute(string Method, string Pattern, string Permission, object? Body)
    {
        public string Key => $"{Method} {Pattern}";

        // Ids are random: authorization is decided before the handler looks anything up.
        public string Path => PlaceholderRegex().Replace(Pattern, _ => Guid.NewGuid().ToString());
    }

    private static readonly IReadOnlyList<StaffRoute> StaffRoutes =
    [
        new("POST", "/v1/exams", RbacCatalog.PermissionCodes.ExamManage,
            new { name = "Authorization Test Exam" }),
        new("GET", "/v1/exams", RbacCatalog.PermissionCodes.ExamRead, null),
        new("GET", "/v1/exams/{examId:guid}", RbacCatalog.PermissionCodes.ExamRead, null),
        new("PUT", "/v1/exams/{examId:guid}/schedule", RbacCatalog.PermissionCodes.ExamManage,
            new { scheduledStartTime = DateTime.UtcNow.AddDays(1), scheduledEndTime = DateTime.UtcNow.AddDays(2) }),
        new("POST", "/v1/exams/{examId:guid}/sections", RbacCatalog.PermissionCodes.ExamManage,
            new { name = "Section" }),
        new("POST", "/v1/exams/{examId:guid}/sections/{sectionId:guid}/questions", RbacCatalog.PermissionCodes.ExamManage,
            new { questionId = Guid.NewGuid() }),
        new("POST", "/v1/exams/{examId:guid}/publish", RbacCatalog.PermissionCodes.ExamPublish, null),

        new("POST", "/v1/questions", RbacCatalog.PermissionCodes.QuestionManage,
            new { text = "Q?", options = new[] { new { text = "A", isCorrect = true }, new { text = "B", isCorrect = false } } }),
        new("GET", "/v1/questions", RbacCatalog.PermissionCodes.QuestionManage, null),
        new("GET", "/v1/questions/{questionId:guid}", RbacCatalog.PermissionCodes.QuestionManage, null),

        new("POST", "/v1/batches", RbacCatalog.PermissionCodes.BatchManage,
            new { examId = Guid.NewGuid(), name = "Authorization Test Batch", maxMembers = 10 }),
        new("POST", "/v1/batches/{batchId}/members", RbacCatalog.PermissionCodes.BatchManage,
            new { email = "member@example.com" }),
        new("POST", "/v1/batches/{batchId}/activate", RbacCatalog.PermissionCodes.BatchManage, null),
        new("POST", "/v1/batches/{batchId}/close", RbacCatalog.PermissionCodes.BatchManage, null),

        new("POST", "/v1/invites", RbacCatalog.PermissionCodes.InviteManage,
            new { examId = Guid.NewGuid(), email = "invitee@example.com" }),
        new("GET", "/v1/invites", RbacCatalog.PermissionCodes.InviteManage, null),
        new("POST", "/v1/invites/{inviteId}/codes", RbacCatalog.PermissionCodes.InviteManage,
            new { expiryHours = 24 }),
        new("POST", "/v1/invites/{inviteId}/decline", RbacCatalog.PermissionCodes.InviteManage, null),
        new("POST", "/v1/invites/{inviteId}/revoke", RbacCatalog.PermissionCodes.InviteManage, null),

        new("POST", "/v1/guardians", RbacCatalog.PermissionCodes.GuardianLinkManage,
            new { email = "guardian@example.com", fullName = "Authorization Test Guardian" }),
        new("POST", "/v1/guardians/{guardianId}/links", RbacCatalog.PermissionCodes.GuardianLinkManage,
            new { candidateId = Guid.NewGuid(), candidateEmail = "candidate@example.com" }),
        new("DELETE", "/v1/guardians/{guardianId}/links/{candidateId}", RbacCatalog.PermissionCodes.GuardianLinkManage, null),
        new("DELETE", "/v1/guardians/{guardianId}/candidates/{candidateId}", RbacCatalog.PermissionCodes.GuardianLinkManage, null),
    ];

    // Routes under the module prefixes that deliberately ask for a signed-in caller only. The guardian
    // verifying their own link cannot be a staff action; the guardian model behind it is redesigned later.
    private static readonly string[] SelfServiceRoutes = ["POST /v1/guardians/links/verify", "POST /v1/invites/accept"];

    private static readonly string[] ModulePrefixes = ["/v1/exams", "/v1/batches", "/v1/invites", "/v1/guardians", "/v1/questions"];

    public static TheoryData<string> StaffRouteKeys => [.. StaffRoutes.Select(r => r.Key)];

    public static TheoryData<string> AllRouteKeys => [.. StaffRoutes.Select(r => r.Key).Concat(SelfServiceRoutes)];

    [GeneratedRegex(@"\{[^}]+\}")]
    private static partial Regex PlaceholderRegex();

    // ---- the routes carry the policies the access matrix relies on --------------------------------

    [Fact]
    public void EveryRouteOfTheModules_IsClassifiedAndStaffRoutesNamePermissionPolicies()
    {
        var routes = EndpointAuthorizationInspector.ListRoutes(factory.Services)
            .Where(r => ModulePrefixes.Any(prefix => r.Pattern.StartsWith(prefix, StringComparison.Ordinal)))
            .ToList();

        // A route added without being listed here fails: whoever adds it must decide who may call it.
        var expectedKeys = StaffRoutes.Select(r => r.Key).Concat(SelfServiceRoutes).Order(StringComparer.Ordinal);
        Assert.Equal(expectedKeys, routes.Select(r => r.Key).Order(StringComparer.Ordinal));

        foreach (var staffRoute in StaffRoutes)
        {
            var route = routes.Single(r => r.Key == staffRoute.Key);
            Assert.Equal([staffRoute.Permission], route.PermissionCodes);
        }

        foreach (var key in SelfServiceRoutes)
        {
            var route = routes.Single(r => r.Key == key);
            Assert.True(route.RequiresAuthorization, $"{key} must at least require a signed-in caller.");
            Assert.Empty(route.PermissionCodes);
        }
    }

    // ---- anonymous callers ------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllRouteKeys))]
    public async Task Route_WithoutAuth_Returns401(string routeKey)
    {
        var route = FindRoute(routeKey);
        using var client = factory.CreateClient();

        var response = await SendAsync(client, route);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---- who may call what ------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(StaffRouteKeys))]
    public async Task Route_AllowsExactlyTheRolesHoldingItsPermission(string routeKey)
    {
        var route = FindRoute(routeKey);

        foreach (var role in RbacCatalog.Roles)
        {
            using var client = AuthorizedClient(await factory.SignInAsAsync(role.Name));

            var response = await SendAsync(client, route);

            var mayCall = role.PermissionCodes.Contains(route.Permission);
            if (mayCall)
            {
                // What the handler then answers is not this test's business (an unknown id is a 404 or
                // a 500 depending on the route today); only that authorization let the call through.
                Assert.True(
                    response.StatusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden),
                    $"{role.Name} holds {route.Permission} but {route.Key} answered {(int)response.StatusCode}.");
            }
            else
            {
                Assert.True(
                    response.StatusCode == HttpStatusCode.Forbidden,
                    $"{role.Name} lacks {route.Permission} but {route.Key} answered {(int)response.StatusCode}.");
            }
        }
    }

    [Fact]
    public async Task GuardianVerifyLink_IsSelfService_AndNotRejectedForLackingAStaffPermission()
    {
        using var client = AuthorizedClient(await factory.SignInAsAsync(RbacCatalog.RoleNames.Guardian));

        var response = await client.PostAsJsonAsync("/v1/guardians/links/verify", new { verificationToken = "token" });

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---- named scenarios the access matrix exists for ---------------------------------------------

    [Fact]
    public async Task Candidate_CreateExam_Returns403()
    {
        using var client = AuthorizedClient(await factory.SignInAsAsync(RbacCatalog.RoleNames.Candidate));

        var response = await client.PostAsJsonAsync("/v1/exams", new { name = "Candidate Exam" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Candidate_CreateBatch_Returns403_AndCreatesNothing()
    {
        using var client = AuthorizedClient(await factory.SignInAsAsync(RbacCatalog.RoleNames.Candidate));
        var batchesBefore = await CountBatchesAsync();

        var response = await client.PostAsJsonAsync(
            "/v1/batches", new { examId = Guid.NewGuid(), name = "Candidate Batch", maxMembers = 10 });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(batchesBefore, await CountBatchesAsync());
    }

    [Fact]
    public async Task Candidate_ActivateBatch_Returns403()
    {
        using var client = AuthorizedClient(await factory.SignInAsAsync(RbacCatalog.RoleNames.Candidate));

        var response = await client.PostAsync($"/v1/batches/{Guid.NewGuid()}/activate", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Candidate_CreateInvite_Returns403()
    {
        using var client = AuthorizedClient(await factory.SignInAsAsync(RbacCatalog.RoleNames.Candidate));

        var response = await client.PostAsJsonAsync(
            "/v1/invites", new { examId = Guid.NewGuid(), batchMemberId = Guid.NewGuid(), email = "someone@example.com" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Candidate_GenerateInviteCode_Returns403()
    {
        using var client = AuthorizedClient(await factory.SignInAsAsync(RbacCatalog.RoleNames.Candidate));

        var response = await client.PostAsJsonAsync($"/v1/invites/{Guid.NewGuid()}/codes", new { expiryHours = 24 });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Candidate_RevokeInvite_Returns403()
    {
        using var client = AuthorizedClient(await factory.SignInAsAsync(RbacCatalog.RoleNames.Candidate));

        var response = await client.PostAsync($"/v1/invites/{Guid.NewGuid()}/revoke", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Candidate_AcceptSomeoneElsesInvite_Returns403()
    {
        // Accepting is open to any signed-in candidate, but only with the invite's code and only from the
        // account that holds the invited address: holding someone else's code is not enough.
        using var admin = await factory.AdminClientAsync();
        var question = await ExamScenarios.CreateQuestionAsync(admin, "Q?", "A", "B");
        var examId = await ExamScenarios.CreateExamAsync(admin, "Someone Else's Exam", [question], TimeSpan.FromHours(-1));
        var invite = await ExamScenarios.InviteAsync(admin, examId, ExamScenarios.UniqueEmail());
        var code = ExamScenarios.CodeFromLink(invite.GetProperty("inviteLink").GetString()!);
        using var client = AuthorizedClient(await factory.SignInAsAsync(RbacCatalog.RoleNames.Candidate));

        var response = await client.PostAsJsonAsync("/v1/invites/accept", new { code });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Guardian_CreateGuardianRecord_Returns403()
    {
        using var client = AuthorizedClient(await factory.SignInAsAsync(RbacCatalog.RoleNames.Guardian));

        var response = await client.PostAsJsonAsync(
            "/v1/guardians", new { email = "guardian@example.com", fullName = "Self Registered Guardian" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task InstituteTeacher_CreateBatch_Returns201()
    {
        using var client = AuthorizedClient(await factory.SignInAsAsync(RbacCatalog.RoleNames.InstituteTeacher));

        var response = await client.PostAsJsonAsync(
            "/v1/batches", new { examId = Guid.NewGuid(), name = "Teacher Batch", maxMembers = 25 });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task InstituteTeacher_CreateInvite_Returns201()
    {
        using var admin = await factory.AdminClientAsync();
        var question = await ExamScenarios.CreateQuestionAsync(admin, "Q?", "A", "B");
        var examId = await ExamScenarios.CreateExamAsync(admin, "Teacher Invite Exam", [question], TimeSpan.FromHours(-1));
        using var client = AuthorizedClient(await factory.SignInAsAsync(RbacCatalog.RoleNames.InstituteTeacher));

        var response = await client.PostAsJsonAsync("/v1/invites", new { examId, email = "invitee@example.com" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task InstituteTeacher_CreateExam_Returns403()
    {
        using var client = AuthorizedClient(await factory.SignInAsAsync(RbacCatalog.RoleNames.InstituteTeacher));

        var response = await client.PostAsJsonAsync("/v1/exams", new { name = "Teacher Exam" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task InstituteTeacher_CreateGuardian_Returns403()
    {
        using var client = AuthorizedClient(await factory.SignInAsAsync(RbacCatalog.RoleNames.InstituteTeacher));

        var response = await client.PostAsJsonAsync(
            "/v1/guardians", new { email = "guardian@example.com", fullName = "Teacher Made Guardian" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ExamAdmin_CreateExam_Returns201()
    {
        using var client = AuthorizedClient(await factory.SignInAsAsync(RbacCatalog.RoleNames.ExamAdmin));

        var response = await client.PostAsJsonAsync("/v1/exams", new { name = "Admin Exam" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task ExamAdmin_CreateGuardian_Returns201()
    {
        using var client = AuthorizedClient(await factory.SignInAsAsync(RbacCatalog.RoleNames.ExamAdmin));

        var response = await client.PostAsJsonAsync(
            "/v1/guardians", new { email = "guardian@example.com", fullName = "Admin Made Guardian" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // ---- helpers ----------------------------------------------------------------------------------

    private static StaffRoute FindRoute(string routeKey) =>
        StaffRoutes.SingleOrDefault(r => r.Key == routeKey)
        ?? new StaffRoute(
            routeKey.Split(' ')[0],
            routeKey.Split(' ')[1],
            Permission: string.Empty,
            Body: new { verificationToken = "token" });

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, StaffRoute route)
    {
        using var request = new HttpRequestMessage(new HttpMethod(route.Method), route.Path);
        if (route.Body is not null)
        {
            request.Content = JsonContent.Create(route.Body);
        }

        // Awaited here, not returned: the request is disposed when this method exits, and the test host reads its body lazily.
        return await client.SendAsync(request);
    }

    private HttpClient AuthorizedClient(SignedInTestUser caller)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", caller.AccessToken);
        return client;
    }

    private async Task<int> CountBatchesAsync()
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<BatchDbContext>().Batches.CountAsync();
    }
}
