using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ExamPlatform.Modules.Identity.Domain.Rbac;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// A staff member logs an incident detected long ago, sees it listed as overdue ahead of a newer one, and then records a status
/// change that clears the flag and keeps who made it and when (FR-52, NFR-13). Runs against the real Host, so the route
/// permission, the migration and the status history are all exercised together.
/// </summary>
public sealed class IncidentFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task LogIncident_ListsItOverdueOldestFirst_ThenAStatusChangeClearsTheFlagAndIsRecorded()
    {
        var staff = await factory.SignInAsAsync(RbacCatalog.RoleNames.SuperAdmin);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staff.AccessToken);

        // Detected seven hours ago, so its six-hour window had closed before anyone logged it.
        var detectedAtUtc = DateTime.UtcNow.AddHours(-7);
        var overdueIncident = await LogAsync(client, detectedAtUtc, "A staff laptop with candidate scores was lost on a train");
        Assert.Equal("Logged", overdueIncident.GetProperty("status").GetString());
        Assert.True(overdueIncident.GetProperty("isOverdue").GetBoolean());
        Assert.Equal(detectedAtUtc.AddHours(6), overdueIncident.GetProperty("escalationDueAtUtc").GetDateTime(), TimeSpan.FromSeconds(1));
        var incidentId = overdueIncident.GetProperty("id").GetGuid();

        // Detected an hour ago, so it falls due later and must be listed after the overdue one.
        var newerIncident = await LogAsync(client, DateTime.UtcNow.AddHours(-1), "A candidate reported a login page that looked wrong");
        var newerId = newerIncident.GetProperty("id").GetGuid();

        // This class has its own database, so the open list holds exactly these two incidents.
        var listed = await ListOpenAsync(client);
        Assert.Equal([incidentId, newerId], listed.Select(i => i.GetProperty("id").GetGuid()));
        Assert.True(listed.Single(i => i.GetProperty("id").GetGuid() == incidentId).GetProperty("isOverdue").GetBoolean());

        // Reporting it to CERT-In, with a note, clears the flag. The change is kept with who made it and when.
        var changed = await client.PostAsJsonAsync(
            $"/v1/incidents/{incidentId}/status",
            new { status = "Reported", note = "Reported to CERT-In by e-mail; reference is in the incident file" });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var body = await changed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Reported", body.GetProperty("status").GetString());
        Assert.False(body.GetProperty("isOverdue").GetBoolean());

        var change = Assert.Single(body.GetProperty("statusChanges").EnumerateArray());
        Assert.Equal("Logged", change.GetProperty("fromStatus").GetString());
        Assert.Equal("Reported", change.GetProperty("toStatus").GetString());
        Assert.Equal(staff.UserId, change.GetProperty("changedById").GetGuid());
        Assert.True(DateTime.UtcNow - change.GetProperty("changedAtUtc").GetDateTime() < TimeSpan.FromMinutes(5));

        // A reported incident stays on the open list, and is no longer flagged.
        var afterChange = await ListOpenAsync(client);
        Assert.False(afterChange.Single(i => i.GetProperty("id").GetGuid() == incidentId).GetProperty("isOverdue").GetBoolean());
    }

    private static async Task<JsonElement> LogAsync(HttpClient client, DateTime detectedAtUtc, string description)
    {
        var response = await client.PostAsJsonAsync("/v1/incidents", new
        {
            description,
            detectedAtUtc,
            category = "DataBreach",
            affectedData = "Candidate names and scores",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<List<JsonElement>> ListOpenAsync(HttpClient client)
    {
        var response = await client.GetAsync("/v1/incidents/open");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await response.Content.ReadFromJsonAsync<JsonElement>();
        return list.EnumerateArray().ToList();
    }
}
