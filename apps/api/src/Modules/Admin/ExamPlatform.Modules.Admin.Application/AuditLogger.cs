using ExamPlatform.Modules.Admin.Application.Ports;
using ExamPlatform.Modules.Admin.Contracts;
using ExamPlatform.Modules.Admin.Domain;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.Admin.Application;

/// <summary>Implements the module's public contract (<see cref="Contracts.IAuditLogger"/>).</summary>
public sealed class AuditLogger(IAuditLogRepository repository, IAdminUnitOfWork unitOfWork, Clock clock) : IAuditLogger
{
    /// <inheritdoc />
    public async Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        var log = AuditLog.Create(
            clock.UtcNow,
            entry.ActorUserId,
            entry.ActorRole,
            entry.Action,
            entry.EntityType,
            entry.EntityId,
            entry.Metadata,
            entry.CorrelationId);

        await repository.AddAsync(log, cancellationToken);

        // A DbUpdateException here is logged with full context and rethrown by the
        // unit of work itself (see AdminUnitOfWork) rather than here — Application
        // stays free of any EF Core-specific exception type (module boundary/DIP).
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
