using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ExamPlatform.Modules.Batch.Endpoints;

namespace ExamPlatform.IntegrationTests;

public class BatchFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task CreateBatch_WithValidData_ReturnsCreatedResponse()
    {
        using var client = AuthorizedClient(await factory.SignInAsAsync("SuperAdmin"));

        var request = new CreateBatchRequest(
            Guid.NewGuid(),
            "Batch A",
            "First batch",
            50);

        var response = await client.PostAsJsonAsync("/v1/batches", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<dynamic>();
        Assert.NotNull(dto);
    }

    [Fact]
    public async Task CreateBatch_WithSpoofedCreatedByInBody_AttributesCreatorFromTokenSubject()
    {
        var caller = await factory.SignInAsAsync("SuperAdmin");
        using var client = AuthorizedClient(caller);

        // An older client (or an attacker) may still send createdBy; it must be ignored.
        var spoofedCreator = Guid.NewGuid();
        var response = await client.PostAsJsonAsync("/v1/batches", new
        {
            examId = Guid.NewGuid(),
            name = "Spoofed Creator Batch",
            description = (string?)null,
            maxMembers = 10,
            createdBy = spoofedCreator,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var batch = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(caller.UserId, batch.GetProperty("createdBy").GetGuid());
        Assert.NotEqual(spoofedCreator, batch.GetProperty("createdBy").GetGuid());
    }

    [Fact]
    public async Task AddBatchMember_WithValidData_ReturnsNoContent()
    {
        using var client = AuthorizedClient(await factory.SignInAsAsync("SuperAdmin"));

        var createRequest = new CreateBatchRequest(Guid.NewGuid(), "Test Batch", null, 50);
        var createResponse = await client.PostAsJsonAsync("/v1/batches", createRequest);
        createResponse.EnsureSuccessStatusCode();
        var batch = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var batchId = batch.GetProperty("id").GetGuid();

        var memberRequest = new AddBatchMemberRequest("candidate@example.com", "+91-9876543210");
        var memberResponse = await client.PostAsJsonAsync($"/v1/batches/{batchId}/members", memberRequest);

        Assert.Equal(HttpStatusCode.NoContent, memberResponse.StatusCode);
    }

    [Fact]
    public async Task CreateBatch_WithoutAuth_ReturnsUnauthorized()
    {
        using var client = factory.CreateClient();

        var request = new CreateBatchRequest(Guid.NewGuid(), "Test Batch", null, 50);
        var response = await client.PostAsJsonAsync("/v1/batches", request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ActivateBatch_AfterMemberAddedInEarlierRequest_Returns204()
    {
        // Each call below is its own request, so the batch is read back from the database every time: the
        // member added first has to be loaded, or the batch looks empty and refuses to activate.
        using var client = AuthorizedClient(await factory.SignInAsAsync("SuperAdmin"));
        var batchId = await CreateBatchAsync(client, maxMembers: 5);
        (await AddMemberAsync(client, batchId, "member@example.com")).EnsureSuccessStatusCode();

        var response = await client.PostAsync($"/v1/batches/{batchId}/activate", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task CloseBatch_AfterActivate_Returns204()
    {
        using var client = AuthorizedClient(await factory.SignInAsAsync("SuperAdmin"));
        var batchId = await CreateBatchAsync(client, maxMembers: 5);
        (await AddMemberAsync(client, batchId, "member@example.com")).EnsureSuccessStatusCode();
        (await client.PostAsync($"/v1/batches/{batchId}/activate", null)).EnsureSuccessStatusCode();

        var response = await client.PostAsync($"/v1/batches/{batchId}/close", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task ActivateBatch_WithoutMembers_Returns400()
    {
        using var client = AuthorizedClient(await factory.SignInAsAsync("SuperAdmin"));
        var batchId = await CreateBatchAsync(client, maxMembers: 5);

        var response = await client.PostAsync($"/v1/batches/{batchId}/activate", null);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "invalid_batch_config");
    }

    [Theory]
    [InlineData("activate")]
    [InlineData("close")]
    public async Task BatchLifecycle_UnknownBatch_Returns404WithTitleBatchNotFound(string action)
    {
        using var client = AuthorizedClient(await factory.SignInAsAsync("SuperAdmin"));

        var response = await client.PostAsync($"/v1/batches/{Guid.NewGuid()}/{action}", null);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "batch_not_found");
    }

    [Fact]
    public async Task AddMember_UnknownBatch_Returns404WithTitleBatchNotFound()
    {
        using var client = AuthorizedClient(await factory.SignInAsAsync("SuperAdmin"));

        var response = await AddMemberAsync(client, Guid.NewGuid(), "member@example.com");

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "batch_not_found");
    }

    [Fact]
    public async Task AddMember_SameEmailInSeparateRequests_Returns409_EvenInAnotherCase()
    {
        using var client = AuthorizedClient(await factory.SignInAsAsync("SuperAdmin"));
        var batchId = await CreateBatchAsync(client, maxMembers: 5);
        (await AddMemberAsync(client, batchId, "member@example.com")).EnsureSuccessStatusCode();

        var response = await AddMemberAsync(client, batchId, "  Member@Example.COM ");

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "duplicate_member");
        Assert.DoesNotContain("member@example.com", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AddMember_BeyondCapacityInSeparateRequests_Returns400()
    {
        using var client = AuthorizedClient(await factory.SignInAsAsync("SuperAdmin"));
        var batchId = await CreateBatchAsync(client, maxMembers: 1);
        (await AddMemberAsync(client, batchId, "first@example.com")).EnsureSuccessStatusCode();

        var response = await AddMemberAsync(client, batchId, "second@example.com");

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "invalid_batch_config");
    }

    [Fact]
    public async Task AddMember_SameEmailConcurrently_OneSucceedsAndTheOtherGets409()
    {
        using var client = AuthorizedClient(await factory.SignInAsAsync("SuperAdmin"));
        var batchId = await CreateBatchAsync(client, maxMembers: 5);

        var responses = await Task.WhenAll(
            AddMemberAsync(client, batchId, "racer@example.com"),
            AddMemberAsync(client, batchId, "racer@example.com"));

        // Both requests can pass the aggregate's own duplicate check; the unique index decides, and the
        // loser must still answer with the typed 409 rather than a 500.
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.NoContent));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
    }

    [Theory]
    [InlineData("not-an-email", null)]
    [InlineData("member@example.com", "+91-98765432101234567890")]
    [InlineData("member@example.com", "call me")]
    public async Task AddMember_WithUnusableContact_Returns400WithTitleInvalidBatchMember(string email, string? phone)
    {
        using var client = AuthorizedClient(await factory.SignInAsAsync("SuperAdmin"));
        var batchId = await CreateBatchAsync(client, maxMembers: 5);

        var response = await AddMemberAsync(client, batchId, email, phone);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "invalid_batch_member");
    }

    [Fact]
    public async Task ImportRoster_SeatsTheGoodRows_AndReportsTheBadOnesByRowNumber()
    {
        using var client = AuthorizedClient(await factory.SignInAsAsync("SuperAdmin"));
        var batchId = await CreateBatchAsync(client, maxMembers: 10);
        var first = UniqueEmail();
        var second = UniqueEmail();

        var response = await ImportRosterAsync(client, batchId, $"email,phone,name\r\n{first},+91 98765 43210,Amy\r\nnot-an-address,,Bad\r\n{second.ToUpperInvariant()},,Ben\r\n");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var report = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal([2, 4], report.GetProperty("added").EnumerateArray().Select(a => a.GetProperty("row").GetInt32()));
        var skipped = Assert.Single(report.GetProperty("skipped").EnumerateArray());
        Assert.Equal(3, skipped.GetProperty("row").GetInt32());
        Assert.Equal("invalid_email", skipped.GetProperty("code").GetString());
        Assert.Equal(["name"], report.GetProperty("ignoredColumns").EnumerateArray().Select(c => c.GetString()!.ToLowerInvariant()));

        // The report names rows, never addresses, and the seats are really stored (read back in a new request).
        Assert.DoesNotContain("@", await response.Content.ReadAsStringAsync());
        var members = await client.GetFromJsonAsync<JsonElement>($"/v1/batches/{batchId}/members");
        var stored = members.EnumerateArray().Select(m => m.GetProperty("email").GetString()!).Order().ToArray();
        Assert.Equal(new[] { first, second }.Order(), stored);
    }

    [Fact]
    public async Task ImportRoster_TheSameFileTwice_SecondTimeSeatsNobodyAndSaysSo()
    {
        using var client = AuthorizedClient(await factory.SignInAsAsync("SuperAdmin"));
        var batchId = await CreateBatchAsync(client, maxMembers: 10);
        var csv = $"{UniqueEmail()}\r\n{UniqueEmail()}\r\n";
        (await ImportRosterAsync(client, batchId, csv)).EnsureSuccessStatusCode();

        var response = await ImportRosterAsync(client, batchId, csv);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var report = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(report.GetProperty("added").EnumerateArray());
        Assert.All(report.GetProperty("skipped").EnumerateArray(), s => Assert.Equal("already_in_batch", s.GetProperty("code").GetString()));
        var members = await client.GetFromJsonAsync<JsonElement>($"/v1/batches/{batchId}/members");
        Assert.Equal(2, members.GetArrayLength());
    }

    [Fact]
    public async Task ImportRoster_WhenTheBatchFillsUp_KeepsTheFirstRowsAndReportsTheRest()
    {
        using var client = AuthorizedClient(await factory.SignInAsAsync("SuperAdmin"));
        var batchId = await CreateBatchAsync(client, maxMembers: 2);

        var response = await ImportRosterAsync(client, batchId, $"{UniqueEmail()}\r\n{UniqueEmail()}\r\n{UniqueEmail()}\r\n");

        var report = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal([1, 2], report.GetProperty("added").EnumerateArray().Select(a => a.GetProperty("row").GetInt32()));
        var skipped = Assert.Single(report.GetProperty("skipped").EnumerateArray());
        Assert.Equal((3, "batch_full"), (skipped.GetProperty("row").GetInt32(), skipped.GetProperty("code").GetString()));
    }

    [Fact]
    public async Task ImportRoster_UnknownBatch_Returns404WithTitleBatchNotFound()
    {
        using var client = AuthorizedClient(await factory.SignInAsAsync("SuperAdmin"));

        var response = await ImportRosterAsync(client, Guid.NewGuid(), UniqueEmail());

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "batch_not_found");
    }

    [Theory]
    [InlineData("")]
    [InlineData("email\r\n")]
    [InlineData("phone\r\n+919876543210\r\n")]
    public async Task ImportRoster_WithAFileThatHasNothingToImport_Returns400WithTitleRosterUnreadable(string csv)
    {
        using var client = AuthorizedClient(await factory.SignInAsAsync("SuperAdmin"));
        var batchId = await CreateBatchAsync(client, maxMembers: 5);

        var response = await ImportRosterAsync(client, batchId, csv);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "roster_unreadable");
    }

    [Fact]
    public async Task ImportRoster_WithoutABody_Returns400WithTitleRosterUnreadable()
    {
        using var client = AuthorizedClient(await factory.SignInAsAsync("SuperAdmin"));
        var batchId = await CreateBatchAsync(client, maxMembers: 5);

        var response = await client.PostAsJsonAsync($"/v1/batches/{batchId}/members/import", new { });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "roster_unreadable");
    }

    [Fact]
    public async Task ImportRoster_WithMoreRowsThanOneImportTakes_Returns400_AndSeatsNobody()
    {
        using var client = AuthorizedClient(await factory.SignInAsAsync("SuperAdmin"));
        var batchId = await CreateBatchAsync(client, maxMembers: 2000);
        var csv = string.Join("\r\n", Enumerable.Range(1, 1001).Select(i => $"bulk{i}-{Guid.NewGuid():N}@example.com"));

        var response = await ImportRosterAsync(client, batchId, csv);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "roster_too_large");
        var members = await client.GetFromJsonAsync<JsonElement>($"/v1/batches/{batchId}/members");
        Assert.Equal(0, members.GetArrayLength());
    }

    [Fact]
    public async Task ImportRoster_WithoutAuth_ReturnsUnauthorized()
    {
        using var client = factory.CreateClient();

        var response = await ImportRosterAsync(client, Guid.NewGuid(), UniqueEmail());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateBatch_WithZeroMaxMembers_Returns400()
    {
        using var client = AuthorizedClient(await factory.SignInAsAsync("SuperAdmin"));

        var response = await client.PostAsJsonAsync("/v1/batches", new CreateBatchRequest(Guid.NewGuid(), "Batch", null, 0));

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "invalid_batch_config");
    }

    // ---- taking a member out ------------------------------------------------------------------------

    private static async Task<IReadOnlyList<(Guid Id, string Email)>> MembersAsync(HttpClient client, Guid batchId)
    {
        var members = await client.GetFromJsonAsync<JsonElement>($"/v1/batches/{batchId}/members");
        return [.. members.EnumerateArray().Select(m => (m.GetProperty("id").GetGuid(), m.GetProperty("email").GetString()!))];
    }

    [Fact]
    public async Task RemoveMember_TakesThemOffTheRoster_FreesTheSeat_AndLetsTheAddressBeAddedAgain()
    {
        using var client = AuthorizedClient(await factory.SignInAsAsync("SuperAdmin"));
        var batchId = await CreateBatchAsync(client, maxMembers: 2);
        (await AddMemberAsync(client, batchId, "keep@example.com")).EnsureSuccessStatusCode();
        (await AddMemberAsync(client, batchId, "leave@example.com")).EnsureSuccessStatusCode();
        await AssertProblemAsync(await AddMemberAsync(client, batchId, "third@example.com"), HttpStatusCode.BadRequest, "invalid_batch_config");
        var leaving = (await MembersAsync(client, batchId)).Single(m => m.Email == "leave@example.com").Id;

        var removed = await client.DeleteAsync($"/v1/batches/{batchId}/members/{leaving}");

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(["keep@example.com"], (await MembersAsync(client, batchId)).Select(m => m.Email));

        // The seat is free again, and the same address can be given one afresh.
        (await AddMemberAsync(client, batchId, "leave@example.com")).EnsureSuccessStatusCode();
        Assert.Equal(["keep@example.com", "leave@example.com"], (await MembersAsync(client, batchId)).Select(m => m.Email).Order());
    }

    [Fact]
    public async Task RemoveMember_LeavesTheBatchCountAtTheSeatsStillHeld()
    {
        using var client = AuthorizedClient(await factory.SignInAsAsync("SuperAdmin"));
        var batchId = await CreateBatchAsync(client, maxMembers: 5);
        (await AddMemberAsync(client, batchId, "a@example.com")).EnsureSuccessStatusCode();
        (await AddMemberAsync(client, batchId, "b@example.com")).EnsureSuccessStatusCode();
        var leaving = (await MembersAsync(client, batchId)).Single(m => m.Email == "a@example.com").Id;

        (await client.DeleteAsync($"/v1/batches/{batchId}/members/{leaving}")).EnsureSuccessStatusCode();

        var batches = await client.GetFromJsonAsync<JsonElement>("/v1/batches");
        var batch = batches.EnumerateArray().Single(b => b.GetProperty("id").GetGuid() == batchId);
        Assert.Equal(1, batch.GetProperty("activeMemberCount").GetInt32());
    }

    [Fact]
    public async Task RemoveMember_NobodyOfThatId_Returns404()
    {
        using var client = AuthorizedClient(await factory.SignInAsAsync("SuperAdmin"));
        var batchId = await CreateBatchAsync(client, maxMembers: 5);
        (await AddMemberAsync(client, batchId, "a@example.com")).EnsureSuccessStatusCode();

        var response = await client.DeleteAsync($"/v1/batches/{batchId}/members/{Guid.NewGuid()}");

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "batch_member_not_found");
        Assert.Single(await MembersAsync(client, batchId));
    }

    [Fact]
    public async Task RemoveMember_AMemberOfAnotherBatch_Returns404_AndLeavesThemWhereTheyAre()
    {
        using var client = AuthorizedClient(await factory.SignInAsAsync("SuperAdmin"));
        var mine = await CreateBatchAsync(client, maxMembers: 5);
        var other = await CreateBatchAsync(client, maxMembers: 5);
        (await AddMemberAsync(client, other, "elsewhere@example.com")).EnsureSuccessStatusCode();
        var theirs = (await MembersAsync(client, other)).Single().Id;

        var response = await client.DeleteAsync($"/v1/batches/{mine}/members/{theirs}");

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "batch_member_not_found");
        Assert.Single(await MembersAsync(client, other));
    }

    [Fact]
    public async Task RemoveMember_TwiceInARow_SecondIs404()
    {
        using var client = AuthorizedClient(await factory.SignInAsAsync("SuperAdmin"));
        var batchId = await CreateBatchAsync(client, maxMembers: 5);
        (await AddMemberAsync(client, batchId, "a@example.com")).EnsureSuccessStatusCode();
        var member = (await MembersAsync(client, batchId)).Single().Id;

        (await client.DeleteAsync($"/v1/batches/{batchId}/members/{member}")).EnsureSuccessStatusCode();
        var second = await client.DeleteAsync($"/v1/batches/{batchId}/members/{member}");

        await AssertProblemAsync(second, HttpStatusCode.NotFound, "batch_member_not_found");
    }

    [Fact]
    public async Task RemoveMember_UnknownBatch_Returns404WithTitleBatchNotFound()
    {
        using var client = AuthorizedClient(await factory.SignInAsAsync("SuperAdmin"));

        var response = await client.DeleteAsync($"/v1/batches/{Guid.NewGuid()}/members/{Guid.NewGuid()}");

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "batch_not_found");
    }

    [Fact]
    public async Task RemoveMember_WithoutAuth_ReturnsUnauthorized()
    {
        using var client = factory.CreateClient();

        var response = await client.DeleteAsync($"/v1/batches/{Guid.NewGuid()}/members/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<Guid> CreateBatchAsync(HttpClient client, int maxMembers)
    {
        var response = await client.PostAsJsonAsync("/v1/batches", new CreateBatchRequest(Guid.NewGuid(), "Batch", null, maxMembers));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> AddMemberAsync(HttpClient client, Guid batchId, string email, string? phone = null) =>
        client.PostAsJsonAsync($"/v1/batches/{batchId}/members", new AddBatchMemberRequest(email, phone));

    private static Task<HttpResponseMessage> ImportRosterAsync(HttpClient client, Guid batchId, string csv) =>
        client.PostAsJsonAsync($"/v1/batches/{batchId}/members/import", new ImportRosterRequest(csv));

    private static string UniqueEmail() => $"roster-{Guid.NewGuid():N}@example.com";

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string errorCode)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(errorCode, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
    }

    private HttpClient AuthorizedClient(SignedInTestUser caller)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", caller.AccessToken);
        return client;
    }
}
