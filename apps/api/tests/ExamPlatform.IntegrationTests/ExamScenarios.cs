using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// Builds exams through the real HTTP API, the way an administrator would, so flow tests that need "a published
/// exam with questions" start from data the API itself produced rather than from rows written behind its back.
/// </summary>
internal static class ExamScenarios
{
    /// <summary>A client signed in as an administrator who may author questions and exams, publish them and invite candidates.</summary>
    public static async Task<HttpClient> AdminClientAsync(this ApiFactory factory)
    {
        var client = factory.CreateClient();
        var admin = await factory.SignInAsAsync("SuperAdmin");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);
        return client;
    }

    /// <summary>A client signed in as a candidate with the given e-mail address.</summary>
    public static async Task<(HttpClient Client, SignedInTestUser User)> CandidateClientAsync(this ApiFactory factory, string? email = null)
    {
        var client = factory.CreateClient();
        var user = await factory.SignInAsAsync("Candidate", email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.AccessToken);
        return (client, user);
    }

    /// <summary>Creates a question with the given options (the first is correct) and returns its id.</summary>
    public static async Task<Guid> CreateQuestionAsync(HttpClient admin, string text, params string[] options)
    {
        var response = await admin.PostAsJsonAsync("/v1/questions", new
        {
            text,
            options = options.Select((option, i) => new { text = option, isCorrect = i == 0 }).ToArray(),
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    /// <summary>
    /// Creates an exam with one section holding <paramref name="questionIds"/>, schedules it
    /// <paramref name="startsIn"/> from now for three hours, and optionally publishes it.
    /// </summary>
    public static async Task<Guid> CreateExamAsync(
        HttpClient admin,
        string name,
        IReadOnlyList<Guid> questionIds,
        TimeSpan startsIn,
        bool publish = true,
        int? durationMinutes = 60)
    {
        var created = await admin.PostAsJsonAsync("/v1/exams", new { name });
        created.EnsureSuccessStatusCode();
        var examId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var section = await admin.PostAsJsonAsync($"/v1/exams/{examId}/sections", new { name = "Section A" });
        section.EnsureSuccessStatusCode();
        var sectionId = (await section.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        foreach (var questionId in questionIds)
        {
            (await admin.PostAsJsonAsync($"/v1/exams/{examId}/sections/{sectionId}/questions", new { questionId })).EnsureSuccessStatusCode();
        }

        var start = DateTime.UtcNow.Add(startsIn);
        (await admin.PutAsJsonAsync($"/v1/exams/{examId}/schedule", new
        {
            scheduledStartTime = start,
            scheduledEndTime = start.AddHours(3),
            durationMinutes,
        })).EnsureSuccessStatusCode();

        if (publish)
        {
            (await admin.PostAsync($"/v1/exams/{examId}/publish", content: null)).EnsureSuccessStatusCode();
        }

        return examId;
    }

    /// <summary>Invites an address to an exam and returns the invite body (which carries the link when no mail server is configured).</summary>
    public static async Task<JsonElement> InviteAsync(HttpClient admin, Guid examId, string email)
    {
        var response = await admin.PostAsJsonAsync("/v1/invites", new { examId, email });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>The code carried by an invitation link.</summary>
    public static string CodeFromLink(string link) => Uri.UnescapeDataString(link[(link.IndexOf("code=", StringComparison.Ordinal) + "code=".Length)..]);

    /// <summary>A fresh e-mail address that no other test uses.</summary>
    public static string UniqueEmail() => $"candidate-{Guid.NewGuid():N}@tests.local";
}
