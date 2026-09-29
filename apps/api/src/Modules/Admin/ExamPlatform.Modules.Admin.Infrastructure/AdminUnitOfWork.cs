using ExamPlatform.Modules.Admin.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ExamPlatform.Modules.Admin.Infrastructure;

/// <summary>
/// EF Core-backed <see cref="IAdminUnitOfWork"/>, wrapping <see cref="AdminDbContext"/>.
/// Losing an audit entry silently is worse than failing the admin action that
/// triggered it, so a failed save is logged with full context and rethrown —
/// never swallowed. Revisit with an outbox pattern once a message queue exists
/// (see ADR 0001), letting audit writes be reliable without blocking the caller.
/// </summary>
public sealed class AdminUnitOfWork(AdminDbContext context, ILogger<AdminUnitOfWork> logger) : IAdminUnitOfWork
{
    /// <inheritdoc />
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "Failed to persist an audit log entry; the triggering action is being failed rather than losing the audit trail silently.");
            throw;
        }
    }
}
