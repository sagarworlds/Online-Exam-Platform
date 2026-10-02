using ExamPlatform.Modules.ExamRuntime.Application.Dtos;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;

namespace ExamPlatform.Modules.ExamRuntime.Application.Queries;

/// <summary>Reads the answer review of one of the candidate's own submitted attempts (FR-32, FR-33).</summary>
public sealed class GetAttemptReviewHandler(AttemptAccess access, AttemptReviewBuilder review)
{
    /// <summary>Returns the review, closing the attempt first if its time has run out.</summary>
    /// <param name="attemptId">The attempt.</param>
    /// <param name="candidateId">The signed-in candidate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="AttemptNotFoundError">No such attempt, or it is someone else's.</exception>
    /// <exception cref="AttemptNotSubmittedError">The attempt is still open.</exception>
    /// <exception cref="ResultsNotReleasedError">The exam's author has not released the answers yet.</exception>
    public async Task<AttemptReviewDto> HandleAsync(Guid attemptId, Guid candidateId, CancellationToken cancellationToken)
    {
        // Ownership first: someone else's attempt answers exactly like a missing one, before anything about it is revealed.
        var (attempt, exam) = await access.LoadOwnedAsync(attemptId, candidateId, cancellationToken);
        return await review.BuildAsync(attempt, exam, cancellationToken);
    }
}
