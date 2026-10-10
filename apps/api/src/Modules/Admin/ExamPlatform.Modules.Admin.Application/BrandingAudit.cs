using ExamPlatform.Modules.Admin.Contracts;
using ExamPlatform.Modules.Admin.Domain;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.Admin.Application;

/// <summary>
/// Writes the audit entries for changes to the institute's branding (FR-40). The actor comes from the request, because the change itself
/// only says what changed, not who changed it.
/// </summary>
public sealed class BrandingAudit(IAuditLogger auditLogger, IRequestContext requestContext)
{
    /// <summary>Records one change to the branding.</summary>
    /// <param name="action">A stable action code, such as <c>Branding.Updated</c>.</param>
    /// <param name="metadata">What changed, for an administrator reviewing the trail.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task RecordAsync(string action, IReadOnlyDictionary<string, string> metadata, CancellationToken cancellationToken) =>
        auditLogger.RecordAsync(
            new AuditEntry(
                ActorUserId: requestContext.UserId,
                ActorRole: requestContext.Role,
                Action: action,
                EntityType: nameof(InstituteBranding),
                EntityId: InstituteBranding.SingletonId.ToString(),
                Metadata: metadata,
                CorrelationId: requestContext.CorrelationId),
            cancellationToken);
}
