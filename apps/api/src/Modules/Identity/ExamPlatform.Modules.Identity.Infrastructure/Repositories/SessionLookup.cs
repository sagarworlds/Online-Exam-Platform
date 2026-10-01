using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Application.Sessions;
using ExamPlatform.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace ExamPlatform.Modules.Identity.Infrastructure.Repositories;

/// <summary>EF Core-backed <see cref="ISessionLookup"/>.</summary>
public sealed class SessionLookup(IdentityDbContext context) : ISessionLookup
{
    // Runs on every authenticated request, so it is one primary-key lookup joined to the
    // owner's status and projected straight into the snapshot: no tracking and none of the
    // roles, permissions or other sessions the User aggregate would load.
    /// <inheritdoc />
    public Task<SessionSnapshot?> FindAsync(Guid sessionId, CancellationToken cancellationToken) =>
        context.Set<UserSession>()
            .AsNoTracking()
            .Where(s => s.Id == sessionId)
            .Join(
                context.Users,
                session => session.UserId,
                user => user.Id,
                (session, user) => new SessionSnapshot(
                    session.UserId, session.ExpiresAtUtc, session.RevokedAtUtc, session.RevokedReason, user.Status))
            .FirstOrDefaultAsync(cancellationToken);
}
