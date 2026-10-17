using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain;

namespace ExamPlatform.Modules.Identity.Application.Commands;

/// <summary>
/// Ends everything that could still sign a student in after the e-mail address or mobile number they signed in with was changed: their
/// sessions, the sign-in codes already sent to the old address or number that nobody has used, and any password reset link. Ending the
/// sessions alone would not be enough. The address may be changed because it was never the student's, and whoever holds it could have
/// asked for a code a moment before, which would sign them in as the student again.
/// </summary>
public sealed class StudentSignInRevoker(IOtpChallengeRepository challenges, IPasswordResetTokenRepository resetTokens)
{
    /// <summary>
    /// Ends the student's sessions and every code and link of theirs that is still usable. The sessions and the codes change with the
    /// account, on the caller's next save; the reset links are revoked straight away, as <see cref="IPasswordResetTokenRepository"/> does,
    /// which is safe to do first since a revoked link only means the student asks for another.
    /// </summary>
    /// <param name="student">The student whose address or number changed.</param>
    /// <param name="nowUtc">The current instant, recorded as when each was ended.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task RevokeAsync(User student, DateTime nowUtc, CancellationToken cancellationToken)
    {
        student.RevokeAllSessions(nowUtc, SessionRevocationReason.ContactChanged);

        foreach (var challenge in await challenges.GetOutstandingForUserAsync(student.Id, nowUtc, cancellationToken))
        {
            challenge.Supersede(nowUtc);
        }

        await resetTokens.RevokeOutstandingForUserAsync(student.Id, nowUtc, cancellationToken);
    }
}
