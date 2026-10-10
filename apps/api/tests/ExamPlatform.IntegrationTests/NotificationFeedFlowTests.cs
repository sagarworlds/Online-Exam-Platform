using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// The signed-in account's own feed (FR-39) over a real database: an account with nothing in its feed sees an empty list, an unknown notice
/// answers as not found, and a page that does not exist is refused. Reading and marking a real notice is in NotificationRunFlowTests, which
/// has the exam and the reminder it needs.
/// </summary>
public sealed class NotificationFeedFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task AnAccountWithNoNotices_HasAnEmptyFeedAndNothingUnread()
    {
        var (client, _) = await factory.CandidateClientAsync();
        using var _c = client;

        var feed = await client.GetFromJsonAsync<JsonElement>("/v1/me/notifications");
        Assert.Empty(feed.GetProperty("items").EnumerateArray());
        Assert.Equal(0, feed.GetProperty("totalCount").GetInt32());
        Assert.Equal(0, feed.GetProperty("unreadCount").GetInt32());

        var count = await client.GetFromJsonAsync<JsonElement>("/v1/me/notifications/unread-count");
        Assert.Equal(0, count.GetProperty("unreadCount").GetInt32());
    }

    [Fact]
    public async Task MarkingAnUnknownNotice_IsNotFound_AndMarkingAllWithNothingUnread_MarksNothing()
    {
        var (client, _) = await factory.CandidateClientAsync();
        using var _c = client;

        var missing = await client.PostAsync($"/v1/me/notifications/{Guid.NewGuid()}/read", content: null);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        var problem = await missing.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("notification_not_found", problem.GetProperty("title").GetString());

        var all = await client.PostAsync("/v1/me/notifications/read-all", content: null);
        all.EnsureSuccessStatusCode();
        Assert.Equal(0, (await all.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("marked").GetInt32());
    }

    [Fact]
    public async Task APageThatDoesNotExist_IsRefused()
    {
        var (client, _) = await factory.CandidateClientAsync();
        using var _c = client;

        var response = await client.GetAsync("/v1/me/notifications?page=0");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_page_request", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
    }
}
