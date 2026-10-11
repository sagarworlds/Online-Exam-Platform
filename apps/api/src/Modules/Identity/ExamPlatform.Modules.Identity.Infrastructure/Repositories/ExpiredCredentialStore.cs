using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Application.Retention;
using ExamPlatform.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace ExamPlatform.Modules.Identity.Infrastructure.Repositories;

/// <summary>
/// The <see cref="IExpiredCredentialStore"/> over the Identity database. Deletes run in the database with one statement per kind, so a sweep
/// never loads the rows it removes.
/// </summary>
public sealed class ExpiredCredentialStore(IdentityDbContext context) : IExpiredCredentialStore
{
    /// <inheritdoc />
    public async Task<CredentialPurgeResult> DeleteExpiredAsync(DateTime cutoffUtc, CancellationToken cancellationToken)
    {
        // Not one transaction: a sweep that fails after the first kind leaves that kind deleted, and the next sweep does the rest.
        var oneTimeCodes = await context.OtpChallenges
            .Where(challenge => challenge.ExpiresAtUtc < cutoffUtc)
            .ExecuteDeleteAsync(cancellationToken);
        var resetTokens = await context.PasswordResetTokens
            .Where(token => token.ExpiresAtUtc < cutoffUtc)
            .ExecuteDeleteAsync(cancellationToken);
        var sessions = await context.Set<UserSession>()
            .Where(session => session.ExpiresAtUtc < cutoffUtc)
            .ExecuteDeleteAsync(cancellationToken);

        return new CredentialPurgeResult(oneTimeCodes, resetTokens, sessions);
    }
}
