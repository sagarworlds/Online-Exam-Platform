using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain;
using ExamPlatform.Modules.Identity.Infrastructure;
using ExamPlatform.SharedKernel.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// A user created and signed in by <see cref="TestSessions.SignInAsAsync"/> or
/// <see cref="TestSessions.RegisterViaApiAsync"/>.
/// </summary>
/// <param name="UserId">The new user's id (the token's <c>sub</c> claim).</param>
/// <param name="SessionId">The persisted session's id (the token's <c>sid</c> claim).</param>
/// <param name="Email">The user's email address.</param>
/// <param name="AccessToken">A bearer token minted by the app's own token generator for that session.</param>
public sealed record SignedInTestUser(Guid UserId, Guid SessionId, string Email, string AccessToken);

/// <summary>
/// THE single test-authentication helper for integration tests, in this and every
/// later change. It signs users in with real Identity state instead of hand-rolled
/// JWTs: a persisted user holding a real seeded role, a persisted
/// <see cref="UserSession"/>, and a token minted by the app's own
/// <see cref="ITokenGenerator"/>. The token therefore carries the same <c>sid</c>,
/// role and <c>perm</c> claims a real login would, so it keeps working once the
/// API checks sessions and permissions on every request (FR-2, FR-4). Use
/// <see cref="TestJwtTokenBuilder"/> only for negative tests that need a token
/// with no session behind it.
/// <para>
/// <see cref="SignInAsAsync"/> writes the state straight to the database, so the login
/// endpoints (OTP, password, and the staff password + 2FA step) are skipped by design:
/// a test whose subject is login behaviour itself must drive those endpoints over HTTP.
/// <see cref="RegisterViaApiAsync"/> is the one helper that goes through the API, for
/// tests that depend on what candidate self-registration itself does.
/// </para>
/// </summary>
public static class TestSessions
{
    // Matches the lifetime TestJwtTokenBuilder has always used; long enough for any
    // single test, short enough that a leaked test token is worthless.
    private static readonly TimeSpan DefaultSessionLifetime = TimeSpan.FromHours(1);

    // An adult, so neither helper trips age-based rules unless a test asks for a minor.
    private static readonly DateOnly DefaultDateOfBirth = new(1990, 1, 1);

    /// <summary>
    /// Creates an active user holding a seeded role, starts a session for it, and
    /// returns an access token for that session.
    /// </summary>
    /// <param name="factory">The factory whose database and services the user is created in.</param>
    /// <param name="roleName">
    /// Name of a role seeded by <c>IdentitySeeder</c> (e.g. "SuperAdmin", "ExamAdmin",
    /// "Candidate", "Guardian").
    /// </param>
    /// <param name="email">
    /// The user's email; defaults to a unique <c>{role}-{guid}@tests.local</c> address
    /// so repeated calls never collide on the unique email index.
    /// </param>
    /// <param name="dateOfBirth">The user's date of birth; defaults to 1990-01-01 (an adult).</param>
    /// <param name="password">
    /// A plaintext password to hash and store, for tests that also exercise password
    /// login; when null the user has no password, like an OTP-only candidate.
    /// </param>
    /// <param name="sessionLifetime">
    /// How long the session lasts from the app clock's current time; defaults to one
    /// hour. Tests that advance a fake clock sign in first and pass a lifetime longer
    /// than the advance, so the session is still live at the advanced time.
    /// </param>
    /// <exception cref="InvalidOperationException">No role named <paramref name="roleName"/> is seeded.</exception>
    public static async Task<SignedInTestUser> SignInAsAsync(
        this ApiFactory factory,
        string roleName,
        string? email = null,
        DateOnly? dateOfBirth = null,
        string? password = null,
        TimeSpan? sessionLifetime = null,
        string? phoneNumber = null)
    {
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var identityDb = services.GetRequiredService<IdentityDbContext>();
        var nowUtc = services.GetRequiredService<Clock>().UtcNow;

        // Permissions are loaded with the role because the token generator bakes them
        // into 'perm' claims; without the Include the token would silently carry none.
        var role = await identityDb.Roles
            .Include(r => r.Permissions)
            .SingleOrDefaultAsync(r => r.Name == roleName)
            ?? throw new InvalidOperationException(
                $"Role '{roleName}' is not seeded; use a role name created by IdentitySeeder.");

        var user = User.Register(
            email ?? $"{roleName.ToLowerInvariant()}-{Guid.NewGuid():N}@tests.local",
            phoneNumber,
            dateOfBirth ?? DefaultDateOfBirth,
            displayName: $"Test {roleName}",
            nowUtc);
        user.AssignRole(role);
        user.Activate();

        if (password is not null)
        {
            user.SetPasswordHash(services.GetRequiredService<IPasswordHasher>().Hash(password));
        }

        // Same shape as LoginSessionIssuer: only the hash of a random per-session secret is stored.
        var sessionSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var sessionTokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sessionSecret)));
        var session = user.StartNewSession(
            sessionTokenHash,
            nowUtc,
            nowUtc.Add(sessionLifetime ?? DefaultSessionLifetime),
            deviceFingerprint: null,
            ipAddress: null);

        await identityDb.Users.AddAsync(user);
        await identityDb.SaveChangesAsync();

        var accessToken = services.GetRequiredService<ITokenGenerator>().GenerateAccessToken(user, session);
        return new SignedInTestUser(user.Id, session.Id, user.Email!, accessToken);
    }

    /// <summary>
    /// Signs a new candidate up through the real HTTP flow: <c>POST /v1/auth/register</c>,
    /// then <c>POST /v1/auth/otp/verify</c> with the code captured by
    /// <see cref="CapturingOtpSender"/>. Returns the session that flow started. Use it
    /// instead of <see cref="SignInAsAsync"/> when a test depends on what self-registration
    /// itself does: the role it grants, the account status it sets, or the rules it applies
    /// to the date of birth (e.g. for minors).
    /// </summary>
    /// <param name="factory">The factory whose API the candidate registers through.</param>
    /// <param name="dateOfBirth">The date of birth to register with; defaults to 1990-01-01 (an adult).</param>
    /// <param name="email">
    /// The email to register with; defaults to a unique <c>candidate-{guid}@tests.local</c> address.
    /// </param>
    /// <param name="displayName">The display name to register with.</param>
    /// <exception cref="InvalidOperationException">
    /// The API rejected the registration or the OTP verification; the message carries its response body.
    /// </exception>
    public static async Task<SignedInTestUser> RegisterViaApiAsync(
        this ApiFactory factory,
        DateOnly? dateOfBirth = null,
        string? email = null,
        string displayName = "Test Candidate")
    {
        var address = email ?? $"candidate-{Guid.NewGuid():N}@tests.local";
        using var client = factory.CreateClient();

        var registerResponse = await client.PostAsJsonAsync("/v1/auth/register", new
        {
            email = address,
            phoneNumber = (string?)null,
            dateOfBirth = dateOfBirth ?? DefaultDateOfBirth,
            displayName,
            otpChannel = nameof(OtpChannel.Email),
        });
        var challenge = await ReadSuccessBodyAsync(registerResponse, "POST /v1/auth/register");

        var verifyResponse = await client.PostAsJsonAsync("/v1/auth/otp/verify", new
        {
            otpChallengeId = challenge.GetProperty("otpChallengeId").GetGuid(),
            code = factory.OtpSender.GetLastCode(address),
        });
        var auth = await ReadSuccessBodyAsync(verifyResponse, "POST /v1/auth/otp/verify");

        var accessToken = auth.GetProperty("accessToken").GetString()
            ?? throw new InvalidOperationException("OTP verification succeeded but returned no access token.");
        var userId = Guid.Parse(new JwtSecurityTokenHandler().ReadJwtToken(accessToken).Subject);
        return new SignedInTestUser(userId, auth.GetProperty("sessionId").GetGuid(), address, accessToken);
    }

    // Throws with the response body, not just the status code, so a test broken by a new
    // validation rule (e.g. on date of birth) says which rule rejected the request.
    private static async Task<JsonElement> ReadSuccessBodyAsync(HttpResponseMessage response, string request)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"{request} returned {(int)response.StatusCode}: {body}");
        }

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
