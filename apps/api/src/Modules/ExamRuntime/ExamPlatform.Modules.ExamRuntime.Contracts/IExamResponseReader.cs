namespace ExamPlatform.Modules.ExamRuntime.Contracts;

/// <summary>
/// What other modules may read of how candidates answered an exam (ADR 0001). Item analysis (FR-37) reads it through this abstraction,
/// so ExamRuntime keeps deciding what counts as a result and how an answer is marked.
/// </summary>
public interface IExamResponseReader
{
    /// <summary>
    /// Reads, for one exam, each counted attempt's score and whether each question was answered fully correctly.
    /// </summary>
    /// <remarks>
    /// Only released results are read: an attempt counts when it is submitted, not invalidated, and the exam's release rule has been met at the
    /// moment of the call. While the author holds the results the attempts list is empty and <see cref="ExamResponses.ResultsReleased"/> is
    /// false, so a caller can say why there is nothing to analyse rather than report zero attempts as if nobody sat the exam.
    /// Correctness is an answer-level fact (the chosen options are exactly the correct ones, or a typed answer matches), so no answer key
    /// and no option text leave the module.
    /// </remarks>
    /// <param name="examId">The exam.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The responses, or <see langword="null"/> when no exam has that id.</returns>
    /// <exception cref="ExamPlatform.SharedKernel.Domain.DomainException">An exam's questions cannot be read or marked, so its responses cannot be read.</exception>
    Task<ExamResponses?> ReadAsync(Guid examId, CancellationToken cancellationToken);
}
