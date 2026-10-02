using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.Identity.Application.Sessions;

/// <summary>
/// Decides whether the session an access token names may still be used (FR-4). Run on
/// every authenticated request, so a superseded, logged-out or expired session, or one
/// whose account was locked, stops working at once instead of when its token expires.
/// </summary>
public sealed class SessionValidator(ISessionLookup sessionLookup, LoginEligibilityPolicy eligibilityPolicy, Clock clock)
{
    /// <summary>Checks the token's session against its current stored state.</summary>
    /// <param name="userId">The user the token was issued to (its <c>sub</c> claim).</param>
    /// <param name="sessionId">The session the token was issued for (its <c>sid</c> claim).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see cref="SessionValidationResult.Valid"/>, or the reason the session is refused.</returns>
    public async Task<SessionValidationResult> ValidateAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await sessionLookup.FindAsync(sessionId, cancellationToken);

        // Another user's session is reported exactly like a missing one, so a token tells
        // its holder nothing about sessions it was not issued for.
        if (session is null || session.UserId != userId)
        {
            return SessionValidationResult.Unknown;
        }

        // Checked before revocation: suspending an account also revokes its sessions, and
        // only "account locked" tells the user what is actually wrong, since signing in
        // again cannot help them.
        if (eligibilityPolicy.IsLocked(session.UserStatus))
        {
            return SessionValidationResult.AccountLocked;
        }

        if (session.RevokedAtUtc is not null)
        {
            return session.RevokedReason == SessionRevocationReason.SupersededByNewLogin
                ? SessionValidationResult.Superseded
                : SessionValidationResult.Revoked;
        }

        // The same boundary as UserSession.IsActive: a session is over at its expiry instant.
        return session.ExpiresAtUtc <= clock.UtcNow
            ? SessionValidationResult.Expired
            : SessionValidationResult.Valid;
    }
}
