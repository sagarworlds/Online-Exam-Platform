using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// An e-mail address is stored as it was entered, and a person types theirs as they remember it, which is not always in the same case
/// (a staff member entered "Priya.Sharma@School.edu" for them, and they sign in as "priya.sharma@school.edu"). The forgot-password page
/// and the sign-in code page answer the same way whether or not an account was found, so an address that matched nothing looked exactly
/// like one that worked, and the message simply never arrived (FR-1, FR-3).
/// </summary>
public sealed class EmailAddressCaseFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "a-long-enough-original-password";
    private const string NewPassword = "correct horse battery staple";

    [Fact]
    public async Task APasswordResetAskedForInAnotherCase_IsMailedToTheAddressTheAccountHolds_AndItsLinkWorks()
    {
        var stored = $"Staff.{Guid.NewGuid():N}@Tests.Local";
        var typed = stored.ToLowerInvariant();
        await factory.SignInAsAsync("SuperAdmin", email: stored, password: Password);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/v1/auth/password-reset/request", new { email = typed });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // To the address the account holds, not the text that was typed: a mail host that tells the two apart must not get the link.
        Assert.True(factory.PasswordResetSender.HasSentTo(stored));
        Assert.False(factory.PasswordResetSender.HasSentTo(typed));

        var parts = factory.PasswordResetSender.GetLastCode(stored).Split(':');
        var reset = await client.PostAsJsonAsync(
            "/v1/auth/password-reset/reset", new { passwordResetTokenId = Guid.Parse(parts[0]), token = parts[1], newPassword = NewPassword });
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
    }

    [Fact]
    public async Task APasswordSignInWithTheAddressInAnotherCase_ReachesTheSameAccount_AndItsCodeGoesToTheStoredAddress()
    {
        var stored = $"Staff.{Guid.NewGuid():N}@Tests.Local";
        var typed = stored.ToUpperInvariant();
        await factory.SignInAsAsync("SuperAdmin", email: stored, password: Password);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/v1/auth/login", new { email = typed, password = Password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("requiresTwoFactor").GetBoolean());
        Assert.True(factory.OtpSender.HasSentTo(stored));
        Assert.False(factory.OtpSender.HasSentTo(typed));
    }

    [Fact]
    public async Task ASignInCodeAskedForInAnotherCase_IsSentToTheAddressTheAccountHolds()
    {
        var stored = $"Cand.{Guid.NewGuid():N}@Tests.Local";
        var typed = stored.ToLowerInvariant();
        await factory.SignInAsAsync("Candidate", email: stored);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/v1/auth/otp/request", new { channel = "Email", destination = typed });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(factory.OtpSender.HasSentTo(stored));
        Assert.False(factory.OtpSender.HasSentTo(typed));
    }

    [Fact]
    public async Task TwoAccountsThatDifferOnlyByCase_AreNeverGuessedBetween_AndEachIsStillReachedByItsOwnText()
    {
        var id = Guid.NewGuid().ToString("N");
        var lower = $"dup.{id}@tests.local";
        var upper = $"DUP.{id}@TESTS.LOCAL";
        var neither = $"Dup.{id}@Tests.Local";
        await factory.SignInAsAsync("SuperAdmin", email: lower, password: Password);
        await factory.SignInAsAsync("SuperAdmin", email: upper, password: Password);
        using var client = factory.CreateClient();

        // Both hold this text without regard to case and neither holds it exactly: no account is better than the wrong one.
        var ambiguous = await client.PostAsJsonAsync("/v1/auth/password-reset/request", new { email = neither });
        Assert.Equal(HttpStatusCode.OK, ambiguous.StatusCode);
        Assert.False(factory.PasswordResetSender.HasSentTo(lower));
        Assert.False(factory.PasswordResetSender.HasSentTo(upper));
        Assert.False(factory.PasswordResetSender.HasSentTo(neither));

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/v1/auth/login", new { email = neither, password = Password })).StatusCode);

        // Each is still reached by exactly its own text.
        await client.PostAsJsonAsync("/v1/auth/password-reset/request", new { email = lower });
        await client.PostAsJsonAsync("/v1/auth/password-reset/request", new { email = upper });
        Assert.True(factory.PasswordResetSender.HasSentTo(lower));
        Assert.True(factory.PasswordResetSender.HasSentTo(upper));
        Assert.Equal(
            HttpStatusCode.OK, (await client.PostAsJsonAsync("/v1/auth/login", new { email = upper, password = Password })).StatusCode);
    }

    [Fact]
    public async Task RegisteringAnAddressThatDiffersOnlyByCaseFromAnAccount_IsRefusedAsADuplicate()
    {
        var stored = $"Taken.{Guid.NewGuid():N}@Tests.Local";
        await factory.SignInAsAsync("Candidate", email: stored);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/v1/auth/register",
            new { email = stored.ToLowerInvariant(), dateOfBirth = "1990-01-01", displayName = "Someone Else", otpChannel = "Email" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("duplicate_account", problem.GetProperty("title").GetString());
    }
}
