using ExamPlatform.Modules.Proctoring.Application.Ports;
using ExamPlatform.Modules.Proctoring.Contracts;

namespace ExamPlatform.Modules.Proctoring.Application.Queries;

/// <summary>Answers other modules' questions about the review of an attempt, from this module's own store.</summary>
public sealed class RiskFlagReader(IRiskAssessmentRepository assessments) : IRiskFlagReader
{
    /// <inheritdoc />
    public Task<bool> IsAwaitingReviewAsync(Guid attemptId, CancellationToken cancellationToken) =>
        assessments.HasOpenFlagAsync(attemptId, cancellationToken);
}
