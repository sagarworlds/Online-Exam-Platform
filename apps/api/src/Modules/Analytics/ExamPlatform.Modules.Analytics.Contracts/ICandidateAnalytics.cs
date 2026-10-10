namespace ExamPlatform.Modules.Analytics.Contracts;

/// <summary>
/// A candidate's own performance across the exams they sat (FR-36). Only released results are included, and the candidate is named by the
/// caller, who must take the id from the signed-in user, never from a request, so one candidate cannot read another's figures.
/// </summary>
public interface ICandidateAnalytics
{
    /// <summary>Works out the candidate's score trend and results by section from their released results.</summary>
    /// <param name="candidateId">The candidate whose performance is wanted.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The figures; with no released result the trend and sections are empty rather than an error.</returns>
    Task<CandidateAnalyticsDto> GetAsync(Guid candidateId, CancellationToken cancellationToken);
}
