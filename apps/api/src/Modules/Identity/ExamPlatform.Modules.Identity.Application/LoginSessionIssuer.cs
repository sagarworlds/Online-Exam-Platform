using System.Security.Cryptography;
using ExamPlatform.Modules.Identity.Application.Dtos;
using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.Identity.Application;

/// <summary>
/// Shared "start a session, mint a token" logic used by every flow that
/// completes a login (OTP verify, password login without 2FA) — kept in one
/// place so the session-token hashing and expiry window can't drift between them.
/// Does not commit the unit of work; the calling handler saves once, alongside
/// whatever else changed in the same request.
/// </summary>
public sealed class LoginSessionIssuer(ITokenGenerator tokenGenerator, Clock clock)
{
    // A login session outlives any single exam attempt's own timer (which the future
    // Exam Runtime module enforces separately via its own server-side end_time) — this
    // is just how long the candidate stays signed in to browse/start exams. It is also the
    // access token's lifetime, since the token generator copies the session's expiry.
    private static readonly TimeSpan SessionValidity = TimeSpan.FromHours(12);

    /// <summary>Starts a new session on the user (superseding any existing one, per FR-4) and mints its access token.</summary>
    /// <param name="user">The user to start a session for.</param>
    /// <param name="deviceFingerprint">Client device fingerprint, if captured.</param>
    /// <param name="ipAddress">Client IP address, if captured.</param>
    public AuthResult Issue(User user, string? deviceFingerprint, string? ipAddress)
    {
        // A random per-session secret, hashed for storage; only its hash is persisted, so a
        // future revocation-check middleware can confirm a presented session id is still
        // live without the database ever holding a usable credential.
        var sessionSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var sessionTokenHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sessionSecret)));

        var session = user.StartNewSession(
            sessionTokenHash: sessionTokenHash,
            nowUtc: clock.UtcNow,
            expiresAtUtc: clock.UtcNow.Add(SessionValidity),
            deviceFingerprint: deviceFingerprint,
            ipAddress: ipAddress);

        var accessToken = tokenGenerator.GenerateAccessToken(user, session);
        return AuthResult.Completed(accessToken, session.Id);
    }
}
