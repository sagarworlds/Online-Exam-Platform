using System.Globalization;
using ExamPlatform.Modules.Admin.Contracts;
using ExamPlatform.Modules.ExamAuthoring.Domain.Events;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.ExamAuthoring.Application;

/// <summary>
/// Records exam changes in the audit trail (FR-40). It reacts to the events an exam raises, so the aggregate
/// and its handlers know nothing about auditing; the actor comes from the request, since an event says what
/// happened and not who did it.
/// </summary>
public sealed class ExamAuditTrail(IAuditLogger auditLogger, IRequestContext requestContext)
    : IDomainEventHandler<ExamCreatedEvent>, IDomainEventHandler<ExamPublishedEvent>
{
    /// <inheritdoc />
    public Task HandleAsync(ExamCreatedEvent domainEvent, CancellationToken cancellationToken) =>
        RecordAsync(
            "ExamAuthoring.ExamCreated",
            domainEvent.ExamId,
            new Dictionary<string, string> { ["name"] = domainEvent.Name },
            cancellationToken);

    /// <inheritdoc />
    public Task HandleAsync(ExamPublishedEvent domainEvent, CancellationToken cancellationToken) =>
        RecordAsync(
            "ExamAuthoring.ExamPublished",
            domainEvent.ExamId,
            new Dictionary<string, string>
            {
                ["scheduledStartTime"] = domainEvent.ScheduledStartTime.ToString("O", CultureInfo.InvariantCulture),
            },
            cancellationToken);

    private Task RecordAsync(string action, Guid examId, IReadOnlyDictionary<string, string> metadata, CancellationToken cancellationToken) =>
        auditLogger.RecordAsync(
            new AuditEntry(
                ActorUserId: requestContext.UserId,
                ActorRole: requestContext.Role,
                Action: action,
                EntityType: "Exam",
                EntityId: examId.ToString(),
                Metadata: metadata,
                CorrelationId: requestContext.CorrelationId),
            cancellationToken);
}
