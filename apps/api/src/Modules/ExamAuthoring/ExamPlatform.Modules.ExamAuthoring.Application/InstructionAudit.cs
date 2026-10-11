using ExamPlatform.Modules.Admin.Contracts;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.ExamAuthoring.Application;

/// <summary>
/// Records changes to instruction templates and to exams' instructions in the audit trail (FR-40). The actor comes from the request, since
/// the change only says what happened and not who did it.
/// </summary>
public sealed class InstructionAudit(IAuditLogger auditLogger, IRequestContext requestContext)
{
    /// <summary>Records one change.</summary>
    /// <param name="action">A stable action code, such as <c>ExamAuthoring.InstructionTemplateCreated</c>.</param>
    /// <param name="entityType">The kind of entity changed: <c>InstructionTemplate</c> or <c>Exam</c>.</param>
    /// <param name="entityId">The changed entity's id.</param>
    /// <param name="metadata">What changed, for an administrator reviewing the trail.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task RecordAsync(
        string action,
        string entityType,
        Guid entityId,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken) =>
        auditLogger.RecordAsync(
            new AuditEntry(
                ActorUserId: requestContext.UserId,
                ActorRole: requestContext.Role,
                Action: action,
                EntityType: entityType,
                EntityId: entityId.ToString(),
                Metadata: metadata,
                CorrelationId: requestContext.CorrelationId),
            cancellationToken);
}
