using ExamPlatform.Modules.Admin.Contracts;
using ExamPlatform.Modules.Invite.Domain.Events;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.Invite.Application;

/// <summary>
/// Records invite changes in the audit trail (FR-40). It reacts to the events an invite raises, and the actor
/// comes from the request: staff for create, decline and revoke, the invited candidate for accept.
/// The invited address is left out of every entry (NFR-6): the invite id identifies it, and the trail is read
/// by more people than the invite list is. So is every code: the entry for a code handed over by hand names it by id.
/// </summary>
public sealed class InviteAuditTrail(IAuditLogger auditLogger, IRequestContext requestContext)
    : IDomainEventHandler<InviteCreatedEvent>,
      IDomainEventHandler<InviteAcceptedEvent>,
      IDomainEventHandler<InviteDeclinedEvent>,
      IDomainEventHandler<InviteRevokedEvent>,
      IDomainEventHandler<InviteCodeGeneratedEvent>
{
    /// <inheritdoc />
    public Task HandleAsync(InviteCreatedEvent domainEvent, CancellationToken cancellationToken) =>
        RecordAsync("Invite.Created", domainEvent.InviteId, domainEvent.ExamId, cancellationToken);

    /// <inheritdoc />
    public Task HandleAsync(InviteAcceptedEvent domainEvent, CancellationToken cancellationToken) =>
        RecordAsync("Invite.Accepted", domainEvent.InviteId, domainEvent.ExamId, cancellationToken);

    /// <inheritdoc />
    public Task HandleAsync(InviteDeclinedEvent domainEvent, CancellationToken cancellationToken) =>
        RecordAsync("Invite.Declined", domainEvent.InviteId, domainEvent.ExamId, cancellationToken);

    /// <inheritdoc />
    public Task HandleAsync(InviteRevokedEvent domainEvent, CancellationToken cancellationToken) =>
        RecordAsync("Invite.Revoked", domainEvent.InviteId, domainEvent.ExamId, cancellationToken);

    /// <inheritdoc />
    public Task HandleAsync(InviteCodeGeneratedEvent domainEvent, CancellationToken cancellationToken) =>
        RecordAsync("Invite.CodeGenerated", domainEvent.InviteId, domainEvent.ExamId, cancellationToken, ("codeId", domainEvent.CodeId.ToString()));

    private Task RecordAsync(
        string action, Guid inviteId, Guid examId, CancellationToken cancellationToken, params (string Key, string Value)[] more)
    {
        var metadata = new Dictionary<string, string> { ["examId"] = examId.ToString() };
        foreach (var (key, value) in more)
            metadata[key] = value;

        return auditLogger.RecordAsync(
            new AuditEntry(
                ActorUserId: requestContext.UserId,
                ActorRole: requestContext.Role,
                Action: action,
                EntityType: "Invite",
                EntityId: inviteId.ToString(),
                Metadata: metadata,
                CorrelationId: requestContext.CorrelationId),
            cancellationToken);
    }
}
