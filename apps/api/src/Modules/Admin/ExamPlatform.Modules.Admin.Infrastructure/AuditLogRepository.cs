using ExamPlatform.Modules.Admin.Application.Ports;
using ExamPlatform.Modules.Admin.Domain;
using Microsoft.EntityFrameworkCore;

namespace ExamPlatform.Modules.Admin.Infrastructure;

/// <summary>EF Core-backed <see cref="IAuditLogRepository"/>.</summary>
public sealed class AuditLogRepository(AdminDbContext context) : IAuditLogRepository
{
    /// <inheritdoc />
    public async Task AddAsync(AuditLog entry, CancellationToken cancellationToken) =>
        await context.AuditLogs.AddAsync(entry, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<AuditLog>> SearchAsync(
        string? entityType, Guid? actorUserId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = context.AuditLogs.AsQueryable();

        if (!string.IsNullOrWhiteSpace(entityType))
        {
            query = query.Where(a => a.EntityType == entityType);
        }

        if (actorUserId is not null)
        {
            query = query.Where(a => a.ActorUserId == actorUserId);
        }

        return await query
            .OrderByDescending(a => a.OccurredAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }
}
