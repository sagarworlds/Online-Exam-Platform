using ExamPlatform.Modules.Admin.Contracts;
using ExamPlatform.Modules.Batch.Domain.Events;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.Batch.Application;

/// <summary>
/// Records batch changes in the audit trail (FR-40). It reacts to the events a batch raises, and the actor
/// comes from the request. Members are named by id only, never by address (NFR-6).
/// </summary>
public sealed class BatchAuditTrail(IAuditLogger auditLogger, IRequestContext requestContext)
    : IDomainEventHandler<BatchCreatedEvent>,
      IDomainEventHandler<BatchActivatedEvent>,
      IDomainEventHandler<BatchClosedEvent>,
      IDomainEventHandler<BatchMemberAddedEvent>,
      IDomainEventHandler<BatchMemberRemovedEvent>
{
    /// <inheritdoc />
    public Task HandleAsync(BatchCreatedEvent domainEvent, CancellationToken cancellationToken) =>
        RecordAsync(
            "Batch.Created",
            domainEvent.BatchId,
            new Dictionary<string, string> { ["examId"] = domainEvent.ExamId.ToString(), ["name"] = domainEvent.Name },
            cancellationToken);

    /// <inheritdoc />
    public Task HandleAsync(BatchActivatedEvent domainEvent, CancellationToken cancellationToken) =>
        RecordAsync("Batch.Activated", domainEvent.BatchId, new Dictionary<string, string> { ["examId"] = domainEvent.ExamId.ToString() }, cancellationToken);

    /// <inheritdoc />
    public Task HandleAsync(BatchClosedEvent domainEvent, CancellationToken cancellationToken) =>
        RecordAsync("Batch.Closed", domainEvent.BatchId, new Dictionary<string, string> { ["examId"] = domainEvent.ExamId.ToString() }, cancellationToken);

    /// <inheritdoc />
    public Task HandleAsync(BatchMemberAddedEvent domainEvent, CancellationToken cancellationToken) =>
        RecordAsync("Batch.MemberAdded", domainEvent.BatchId, new Dictionary<string, string> { ["memberId"] = domainEvent.MemberId.ToString() }, cancellationToken);

    /// <inheritdoc />
    public Task HandleAsync(BatchMemberRemovedEvent domainEvent, CancellationToken cancellationToken) =>
        RecordAsync("Batch.MemberRemoved", domainEvent.BatchId, new Dictionary<string, string> { ["memberId"] = domainEvent.MemberId.ToString() }, cancellationToken);

    private Task RecordAsync(string action, Guid batchId, IReadOnlyDictionary<string, string> metadata, CancellationToken cancellationToken) =>
        auditLogger.RecordAsync(
            new AuditEntry(
                ActorUserId: requestContext.UserId,
                ActorRole: requestContext.Role,
                Action: action,
                EntityType: "Batch",
                EntityId: batchId.ToString(),
                Metadata: metadata,
                CorrelationId: requestContext.CorrelationId),
            cancellationToken);
}
