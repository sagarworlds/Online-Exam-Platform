using ExamPlatform.Modules.ExamRuntime.Application.Dtos;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.ExamRuntime.Application.Queries;

/// <summary>
/// Reads the result of one of the candidate's own submitted attempts (FR-32): the score, where it stands among the exam's released
/// results, and the marks by section.
/// </summary>
public sealed class GetAttemptResultHandler(AttemptAccess access, AttemptReviewBuilder review, IAttemptRepository attempts, Clock clock)
{
    /// <summary>Returns the result, closing the attempt first if its time has run out.</summary>
    /// <param name="attemptId">The attempt.</param>
    /// <param name="candidateId">The signed-in candidate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The candidate's result.</returns>
    /// <exception cref="AttemptNotFoundError">No such attempt, or it is someone else's.</exception>
    /// <exception cref="AttemptNotSubmittedError">The attempt is still open.</exception>
    /// <exception cref="AttemptInvalidatedError">An administrator invalidated the result, so it no longer counts.</exception>
    /// <exception cref="ResultsNotReleasedError">The exam's author has not released the results yet.</exception>
    public async Task<AttemptResultDto> HandleAsync(Guid attemptId, Guid candidateId, CancellationToken cancellationToken)
    {
        var (attempt, exam) = await access.LoadOwnedAsync(attemptId, candidateId, cancellationToken);

        // An invalidated result no longer counts, so it is not placed on the board either (FR-29).
        if (attempt.IsInvalidated)
            throw new AttemptInvalidatedError();

        // The review builder refuses an open attempt and an unreleased result, so this page cannot show either.
        var built = await review.BuildAsync(attempt, exam, cancellationToken);

        // Only other candidates' results are compared, so a retake never ranks against itself. The candidate's own results are excluded in the query.
        var others = await attempts.ListBestScoresOfOtherCandidatesAsync(exam.Id, candidateId, cancellationToken);
        var standing = ResultStanding.Of(built.Score, others);

        // Until the window closes another candidate can still submit and move the rank, so the page must say the rank is not yet final.
        var provisional = clock.UtcNow < exam.EndUtc;

        return new AttemptResultDto(
            built.AttemptId,
            built.ExamId,
            built.ExamName,
            built.Number,
            built.SubmittedAtUtc,
            built.AutoSubmitted,
            built.Score,
            built.MaxScore,
            built.CorrectCount,
            built.WrongCount,
            built.PartialCount,
            built.UnansweredCount,
            built.Sections.Select(SectionOf).ToList(),
            standing.Rank,
            standing.Percentile,
            standing.CohortSize,
            provisional,
            built.ResultVersion);
    }

    private static SectionResultDto SectionOf(ReviewSectionDto section)
    {
        var questions = section.Questions;
        return new SectionResultDto(
            section.Id,
            section.Name,
            questions.Sum(q => q.Marks),
            questions.Count(q => q.Verdict == AnswerVerdict.Correct),
            questions.Count(q => q.Verdict == AnswerVerdict.Wrong),
            questions.Count(q => q.Verdict == AnswerVerdict.Partial),
            questions.Count(q => q.Verdict == AnswerVerdict.Unanswered));
    }
}
