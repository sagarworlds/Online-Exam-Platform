using ExamPlatform.Modules.Admin.Contracts;
using ExamPlatform.Modules.Invite.Domain.Events;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.Invite.Application;

/// <summary>
/// Records invite changes in the audit trail (FR-40). It reacts to the events an invite raises, and the actor
/// comes from the request: staff for create, decline and revoke, the invited candidate for accept.
/// The invited address is left out of every entry (NFR-6), the old one and the new one when it is changed: the invite id identifies
/// it, and the trail is read by more people than the invite list is. So is every code: the entry for a code handed over by hand names it by id.
/// </summary>
public sealed class InviteAuditTrail(IAuditLogger auditLogger, IRequestContext requestContext, IClientInfo clientInfo)
    : IDomainEventHandler<InviteCreatedEvent>,
      IDomainEventHandler<InviteAcceptedEvent>,
      IDomainEventHandler<InviteDeclinedEvent>,
      IDomainEventHandler<InviteRevokedEvent>,
      IDomainEventHandler<InviteCodeGeneratedEvent>,
      IDomainEventHandler<InviteDirectLinkIssuedEvent>,
      IDomainEventHandler<InviteDirectLinkUsedEvent>,
      IDomainEventHandler<InviteEmailChangedEvent>
{
    // The role Identity gives every student. Spelled out because this module may not reference Identity's domain, and a link signs in a
    // student and nothing else, so the actor's role is known without asking.
    private const string StudentRole = "Candidate";

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

    /// <inheritdoc />
    public Task HandleAsync(InviteDirectLinkIssuedEvent domainEvent, CancellationToken cancellationToken) =>
        RecordAsync("Invite.DirectLinkIssued", domainEvent.InviteId, domainEvent.ExamId, cancellationToken, ("codeId", domainEvent.CodeId.ToString()));

    /// <summary>
    /// A direct link signs someone in, so this entry says who and from where. The actor is taken from the event, not the request: the
    /// request that opened the link is anonymous, and a stale or foreign token that came with it must not be named as the actor.
    /// </summary>
    /// <inheritdoc />
    public Task HandleAsync(InviteDirectLinkUsedEvent domainEvent, CancellationToken cancellationToken) =>
        auditLogger.RecordAsync(
            new AuditEntry(
                ActorUserId: domainEvent.UserId,
                ActorRole: StudentRole,
                Action: "Invite.DirectLinkUsed",
                EntityType: "Invite",
                EntityId: domainEvent.InviteId.ToString(),
                Metadata: new Dictionary<string, string>
                {
                    ["examId"] = domainEvent.ExamId.ToString(),
                    ["codeId"] = domainEvent.CodeId.ToString(),
                    ["userId"] = domainEvent.UserId.ToString(),
                    ["accountCreated"] = domainEvent.AccountCreated.ToString(),
                    ["ip"] = clientInfo.IpAddress ?? string.Empty,
                },
                CorrelationId: requestContext.CorrelationId),
            cancellationToken);

    /// <inheritdoc />
    public Task HandleAsync(InviteEmailChangedEvent domainEvent, CancellationToken cancellationToken) =>
        RecordAsync("Invite.EmailChanged", domainEvent.InviteId, domainEvent.ExamId, cancellationToken);

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
