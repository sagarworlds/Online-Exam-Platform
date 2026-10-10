using ExamPlatform.Modules.Admin.Contracts;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.Proctoring.Application;

/// <summary>
/// Records the risk review in the platform's audit trail (FR-40): every scan that writes assessments, and every decision a reviewer makes.
/// Called after the change is committed, as the other modules do (ADR 0002, decision 6).
/// </summary>
public sealed class ProctoringAuditTrail(IAuditLogger auditLogger, IRequestContext requestContext)
{
    /// <summary>Records that a reviewer ran the risk score over an exam.</summary>
    /// <param name="examId">The exam scanned.</param>
    /// <param name="scored">How many attempts were scored.</param>
    /// <param name="flagged">How many of them reached the flag threshold.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task RecordScanAsync(Guid examId, int scored, int flagged, CancellationToken cancellationToken) =>
        auditLogger.RecordAsync(
            new AuditEntry(
                ActorUserId: requestContext.UserId,
                ActorRole: requestContext.Role,
                Action: "Proctoring.RiskScanRun",
                EntityType: "Exam",
                EntityId: examId.ToString(),
                Metadata: new Dictionary<string, string>
                {
                    ["scored"] = scored.ToString(),
                    ["flagged"] = flagged.ToString(),
                },
                CorrelationId: requestContext.CorrelationId),
            cancellationToken);

    /// <summary>Records a reviewer's decision on a flag. The note, if any, is kept with it.</summary>
    /// <param name="assessmentId">The assessment decided on.</param>
    /// <param name="examId">The exam the attempt was sat at.</param>
    /// <param name="attemptId">The attempt the flag is about.</param>
    /// <param name="decision">Reviewed or Dismissed.</param>
    /// <param name="note">The reviewer's note, or null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task RecordDecisionAsync(
        Guid assessmentId, Guid examId, Guid attemptId, string decision, string? note, CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<string, string>
        {
            ["examId"] = examId.ToString(),
            ["attemptId"] = attemptId.ToString(),
        };
        if (note is not null)
            metadata["note"] = note;

        return auditLogger.RecordAsync(
            new AuditEntry(
                ActorUserId: requestContext.UserId,
                ActorRole: requestContext.Role,
                Action: $"Proctoring.RiskFlag{decision}",
                EntityType: "RiskAssessment",
                EntityId: assessmentId.ToString(),
                Metadata: metadata,
                CorrelationId: requestContext.CorrelationId),
            cancellationToken);
    }
}
