namespace ExamPlatform.Modules.ExamRuntime.Contracts;

/// <summary>
/// What other modules may read of a candidate's results (ADR 0001). Analytics reads a candidate's own released results through this
/// abstraction, never through ExamRuntime's Domain, Application or Infrastructure, so ExamRuntime keeps deciding what a result is.
/// </summary>
public interface ICandidateResultReader
{
    /// <summary>
    /// Reads the results the candidate may see now: every submitted attempt of theirs that still counts and whose exam has released its
    /// answers by the moment of the call.
    /// </summary>
    /// <remarks>
    /// Only the results and their section totals are returned, never question text, options or the answer key, so a caller cannot
    /// reconstruct what the candidate was shown. A result held back by the exam's release rule is left out rather than reported as
    /// zero, because a held result says nothing about how the candidate did.
    /// </remarks>
    /// <param name="candidateId">The candidate whose results are wanted; only that candidate's attempts are read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One entry per released attempt, oldest submission first; empty when none has been released.</returns>
    /// <exception cref="ExamPlatform.SharedKernel.Domain.DomainException">
    /// An exam's questions cannot be read or marked, so its results cannot be read.
    /// </exception>
    Task<IReadOnlyList<CandidateResult>> ListReleasedAsync(Guid candidateId, CancellationToken cancellationToken);
}
