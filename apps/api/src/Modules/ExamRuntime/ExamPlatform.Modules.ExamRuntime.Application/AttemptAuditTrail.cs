using ExamPlatform.Modules.Admin.Contracts;
using ExamPlatform.Modules.ExamRuntime.Domain.Events;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.ExamRuntime.Application;

/// <summary>
/// Records what administrators do to a candidate's attempt in the audit trail (FR-29, FR-40). It reacts to the events the attempt
/// raises, so the aggregate and its handlers know nothing about auditing; the actor comes from the request, since an event says what
/// happened and not who did it.
/// </summary>
public sealed class AttemptAuditTrail(IAuditLogger auditLogger, IRequestContext requestContext)
    : IDomainEventHandler<AttemptWarnedEvent>,
        IDomainEventHandler<AttemptPausedEvent>,
        IDomainEventHandler<AttemptResumedEvent>,
        IDomainEventHandler<AttemptTerminatedEvent>,
        IDomainEventHandler<AttemptInvalidatedEvent>
{
    /// <inheritdoc />
    public Task HandleAsync(AttemptWarnedEvent domainEvent, CancellationToken cancellationToken) =>
        RecordAsync("ExamRuntime.AttemptWarned", domainEvent.AttemptId, domainEvent.ExamId, domainEvent.CandidateId,
            new Dictionary<string, string> { ["message"] = domainEvent.Message }, cancellationToken);

    /// <inheritdoc />
    public Task HandleAsync(AttemptPausedEvent domainEvent, CancellationToken cancellationToken) =>
        RecordAsync("ExamRuntime.AttemptPaused", domainEvent.AttemptId, domainEvent.ExamId, domainEvent.CandidateId, null, cancellationToken);

    /// <inheritdoc />
    public Task HandleAsync(AttemptResumedEvent domainEvent, CancellationToken cancellationToken) =>
        RecordAsync("ExamRuntime.AttemptResumed", domainEvent.AttemptId, domainEvent.ExamId, domainEvent.CandidateId,
            new Dictionary<string, string> { ["pausedSeconds"] = domainEvent.PausedSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            cancellationToken);

    /// <inheritdoc />
    public Task HandleAsync(AttemptTerminatedEvent domainEvent, CancellationToken cancellationToken) =>
        RecordAsync("ExamRuntime.AttemptTerminated", domainEvent.AttemptId, domainEvent.ExamId, domainEvent.CandidateId,
            new Dictionary<string, string> { ["reason"] = domainEvent.Reason }, cancellationToken);

    /// <inheritdoc />
    public Task HandleAsync(AttemptInvalidatedEvent domainEvent, CancellationToken cancellationToken) =>
        RecordAsync("ExamRuntime.AttemptInvalidated", domainEvent.AttemptId, domainEvent.ExamId, domainEvent.CandidateId,
            new Dictionary<string, string> { ["reason"] = domainEvent.Reason }, cancellationToken);

    private Task RecordAsync(
        string action, Guid attemptId, Guid examId, Guid candidateId, Dictionary<string, string>? extra, CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<string, string>
        {
            ["examId"] = examId.ToString(),
            ["candidateId"] = candidateId.ToString(),
        };
        if (extra is not null)
            foreach (var (key, value) in extra)
                metadata[key] = value;

        return auditLogger.RecordAsync(
            new AuditEntry(
                ActorUserId: requestContext.UserId,
                ActorRole: requestContext.Role,
                Action: action,
                EntityType: "Attempt",
                EntityId: attemptId.ToString(),
                Metadata: metadata,
                CorrelationId: requestContext.CorrelationId),
            cancellationToken);
    }
}
