using ExamPlatform.Modules.Admin.Domain;

namespace ExamPlatform.Modules.Admin.Application.Ports;

/// <summary>Persistence port for <see cref="AuditLog"/> entries.</summary>
public interface IAuditLogRepository
{
    /// <summary>Begins tracking a new audit entry for insertion on the next unit-of-work commit.</summary>
    /// <param name="entry">The entry to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AddAsync(AuditLog entry, CancellationToken cancellationToken);

    /// <summary>Searches audit entries, most recent first, optionally filtered.</summary>
    /// <param name="entityType">Only entries for this entity type, if given.</param>
    /// <param name="actorUserId">Only entries by this actor, if given.</param>
    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize">Number of entries per page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<AuditLog>> SearchAsync(
        string? entityType, Guid? actorUserId, int page, int pageSize, CancellationToken cancellationToken);
}
