using System.Net.Http.Json;
using System.Text.Json;
using ExamPlatform.Modules.Guardian.Endpoints;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// The guardian side of FR-43 for tests whose candidate is under 18. Staff register a guardian and link the candidate, then the guardian
/// confirms the link with the one-time code the platform issues. Until a link is confirmed, a minor cannot start a new attempt.
/// </summary>
internal static class GuardianScenarios
{
    /// <summary>
    /// Links a new guardian to the candidate and confirms that link, so the candidate may start an attempt.
    /// </summary>
    /// <param name="factory">The API under test. The confirmation is sent without a sign-in, as the guardian's browser would send it.</param>
    /// <param name="staff">A client signed in with <c>guardian.link.manage</c>, which registers and links the guardian.</param>
    /// <param name="candidateId">The candidate's user id. The start gate looks the link up by this id.</param>
    /// <param name="candidateEmail">The candidate's e-mail, recorded on the link.</param>
    /// <exception cref="HttpRequestException">Staff or the guardian's confirmation was refused, so the link is not in force.</exception>
    public static async Task ConfirmGuardianLinkAsync(this ApiFactory factory, HttpClient staff, Guid candidateId, string candidateEmail)
    {
        var registered = await staff.PostAsJsonAsync(
            "/v1/guardians",
            new CreateGuardianRequest($"guardian-{Guid.NewGuid():N}@tests.local", "Gia Guardian", null));
        registered.EnsureSuccessStatusCode();
        var guardianId = (await registered.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var linked = await staff.PostAsJsonAsync($"/v1/guardians/{guardianId}/links", new LinkCandidateRequest(candidateId, candidateEmail));
        linked.EnsureSuccessStatusCode();
        // The test host has no mail server, so the confirmation link comes back to staff rather than going to the guardian's inbox.
        var confirmLink = (await linked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("consentLink").GetString()!;

        using var anonymous = factory.CreateClient();
        var confirmed = await anonymous.PostAsJsonAsync("/v1/guardian-links/verify", new VerifyGuardianLinkRequest(CodeFrom(confirmLink)));
        confirmed.EnsureSuccessStatusCode();
    }

    /// <summary>The one-time code carried by a confirmation link, as the guardian's browser would send it back.</summary>
    private static string CodeFrom(string confirmLink) =>
        Uri.UnescapeDataString(new Uri(confirmLink).Query["?token=".Length..]);
}
