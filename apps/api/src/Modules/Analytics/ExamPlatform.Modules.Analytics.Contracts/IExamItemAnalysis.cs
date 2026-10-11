namespace ExamPlatform.Modules.Analytics.Contracts;

/// <summary>
/// An exam's item analysis (FR-37): how each question performed among the candidates whose results are released. Staff only; the caller must
/// check the permission before asking, since the analysis describes how a cohort of named candidates did, even though it names none of them.
/// </summary>
public interface IExamItemAnalysis
{
    /// <summary>Works out the difficulty and discrimination of each question of an exam.</summary>
    /// <param name="examId">The exam to analyse.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The analysis, with the indices withheld for questions below the minimum cohort size.</returns>
    /// <exception cref="ExamPlatform.SharedKernel.Domain.DomainException">No exam has that id, so there is nothing to analyse.</exception>
    Task<ExamItemAnalysisDto> GetAsync(Guid examId, CancellationToken cancellationToken);
}
