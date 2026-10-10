using ExamPlatform.Modules.Proctoring.Application.Ports;
using ExamPlatform.Modules.Proctoring.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.Proctoring.Application.Commands;

/// <summary>
/// A reviewer dismisses a flag as not a concern. The note is required, so the record says why, and the attempt itself is not touched.
/// </summary>
public sealed class DismissRiskFlagHandler(
    IRiskAssessmentRepository assessments, IProctoringUnitOfWork unitOfWork, Clock clock, ProctoringAuditTrail audit)
{
    /// <summary>Dismisses the flag with its note and records who did it.</summary>
    /// <param name="assessmentId">The flag.</param>
    /// <param name="reviewerId">The reviewer, from their token.</param>
    /// <param name="note">Why the flag is dismissed; required, up to 500 characters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="RiskAssessmentNotFoundError">No flag has that id.</exception>
    /// <exception cref="RiskFlagNotRaisedError">The attempt is not flagged.</exception>
    /// <exception cref="RiskFlagAlreadyDecidedError">A reviewer has already decided on it.</exception>
    /// <exception cref="InvalidRiskDecisionError">The note is blank or too long.</exception>
    public async Task HandleAsync(Guid assessmentId, Guid reviewerId, string? note, CancellationToken cancellationToken)
    {
        var assessment = await assessments.GetByIdAsync(assessmentId, cancellationToken)
            ?? throw new RiskAssessmentNotFoundError(assessmentId);

        assessment.Dismiss(reviewerId, note, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await audit.RecordDecisionAsync(assessment.Id, assessment.ExamId, assessment.AttemptId, "Dismissed", assessment.DecisionNote, cancellationToken);
    }
}
