using ExamPlatform.Modules.Admin.Contracts;
using ExamPlatform.Modules.Identity.Domain.Events;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.Identity.Application;

/// <summary>
/// Records a second sign-in that ended an earlier session in the audit trail (FR-26, FR-40): who it was, and the address and device
/// signature of both sessions, so a staff member looking into an attempt can see that the account was used from two places. It
/// reacts to the event the user raises, so the aggregate knows nothing about auditing.
/// </summary>
/// <remarks>
/// The actor is the account owner themselves: the second sign-in is the request in flight, and there is no signed-in caller yet.
/// An address is recorded here because the audit trail is the platform's security log (CERT-In asks for it); it is never shown to a candidate.
/// </remarks>
public sealed class IdentityAuditTrail(IAuditLogger auditLogger, IRequestContext requestContext) : IDomainEventHandler<SessionSupersededEvent>
{
    /// <inheritdoc />
    public Task HandleAsync(SessionSupersededEvent domainEvent, CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<string, string>
        {
            ["supersededSessionId"] = domainEvent.SupersededSessionId.ToString(),
            ["newSessionId"] = domainEvent.NewSessionId.ToString(),
        };
        Add(metadata, "supersededIp", domainEvent.SupersededIpAddress);
        Add(metadata, "supersededDevice", domainEvent.SupersededDeviceFingerprint);
        Add(metadata, "newIp", domainEvent.NewIpAddress);
        Add(metadata, "newDevice", domainEvent.NewDeviceFingerprint);

        return auditLogger.RecordAsync(
            new AuditEntry(
                ActorUserId: domainEvent.UserId,
                ActorRole: null,
                Action: "Identity.SessionSuperseded",
                EntityType: "User",
                EntityId: domainEvent.UserId.ToString(),
                Metadata: metadata,
                CorrelationId: requestContext.CorrelationId),
            cancellationToken);
    }

    private static void Add(Dictionary<string, string> metadata, string key, string? value)
    {
        if (!string.IsNullOrEmpty(value))
            metadata[key] = value;
    }
}
