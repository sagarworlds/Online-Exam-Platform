using ExamPlatform.Modules.Proctoring.Application.Ports;
using ExamPlatform.Modules.Proctoring.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.Proctoring.Application.Commands;

/// <summary>
/// A reviewer marks a flagged attempt reviewed. The attempt itself is not touched: the candidate's result, access and messages stay as
/// they were.
/// </summary>
public sealed class ReviewRiskFlagHandler(
    IRiskAssessmentRepository assessments, IProctoringUnitOfWork unitOfWork, Clock clock, ProctoringAuditTrail audit)
{
    /// <summary>Marks the flag reviewed and records who did it.</summary>
    /// <param name="assessmentId">The flag.</param>
    /// <param name="reviewerId">The reviewer, from their token.</param>
    /// <param name="note">An optional note, up to 500 characters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="RiskAssessmentNotFoundError">No flag has that id.</exception>
    /// <exception cref="RiskFlagNotRaisedError">The attempt is not flagged.</exception>
    /// <exception cref="RiskFlagAlreadyDecidedError">A reviewer has already decided on it.</exception>
    /// <exception cref="InvalidRiskDecisionError">The note is too long.</exception>
    public async Task HandleAsync(Guid assessmentId, Guid reviewerId, string? note, CancellationToken cancellationToken)
    {
        var assessment = await assessments.GetByIdAsync(assessmentId, cancellationToken)
            ?? throw new RiskAssessmentNotFoundError(assessmentId);

        assessment.MarkReviewed(reviewerId, note, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await audit.RecordDecisionAsync(assessment.Id, assessment.ExamId, assessment.AttemptId, "Reviewed", assessment.DecisionNote, cancellationToken);
    }
}
