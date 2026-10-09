using ExamPlatform.Modules.ExamRuntime.Application.Dtos;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.ExamRuntime.Application.Queries;

/// <summary>Reads the answer review of one of the candidate's own submitted attempts (FR-32, FR-33).</summary>
public sealed class GetAttemptReviewHandler(
    AttemptAccess access, AttemptReviewBuilder review, IDisputeRepository disputes, DisputePolicy disputePolicy, Clock clock)
{
    /// <summary>Returns the review, closing the attempt first if its time has run out.</summary>
    /// <param name="attemptId">The attempt.</param>
    /// <param name="candidateId">The signed-in candidate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="AttemptNotFoundError">No such attempt, or it is someone else's.</exception>
    /// <exception cref="AttemptNotSubmittedError">The attempt is still open.</exception>
    /// <exception cref="AttemptInvalidatedError">An administrator invalidated the result.</exception>
    /// <exception cref="ResultsNotReleasedError">The exam's author has not released the answers yet.</exception>
    public async Task<AttemptReviewDto> HandleAsync(Guid attemptId, Guid candidateId, CancellationToken cancellationToken)
    {
        // Ownership first: someone else's attempt answers exactly like a missing one, before anything about it is revealed.
        var (attempt, exam) = await access.LoadOwnedAsync(attemptId, candidateId, cancellationToken);

        // An invalidated result no longer counts, so it has no answer review either (FR-29).
        if (attempt.IsInvalidated)
            throw new AttemptInvalidatedError();

        var built = await review.BuildAsync(attempt, exam, cancellationToken);

        // What the candidate can do about the key, and what has become of what they already did (FR-31).
        var raised = await disputes.ListForAttemptAsync(attemptId, cancellationToken);
        return built with
        {
            DisputeWindow = disputePolicy.WindowFor(exam, attempt, clock.UtcNow),
            Disputes = raised.Select(DisputeDtoFactory.ForCandidate).ToList(),
        };
    }
}
