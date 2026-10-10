using ExamPlatform.Modules.Proctoring.Domain;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.Proctoring.Application.Ports;

/// <summary>One page of flagged assessments, with the total across all pages.</summary>
/// <param name="Items">The assessments on this page, highest score first.</param>
/// <param name="Total">How many assessments match the filter across every page.</param>
public sealed record RiskFlagPage(IReadOnlyList<RiskAssessment> Items, int Total);

/// <summary>Persistence port for <see cref="RiskAssessment"/>.</summary>
public interface IRiskAssessmentRepository
{
    /// <summary>Starts tracking a new assessment; it is stored when the unit of work saves.</summary>
    /// <param name="assessment">The assessment to add.</param>
    void Add(RiskAssessment assessment);

    /// <summary>Loads every assessment at an exam, with its signals, tracked so that a rescore is saved.</summary>
    /// <param name="examId">The exam.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<RiskAssessment>> ListForExamAsync(Guid examId, CancellationToken cancellationToken);

    /// <summary>Loads one assessment with its signals, tracked so that a decision is saved.</summary>
    /// <param name="assessmentId">The assessment's id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The assessment, or null when none has that id.</returns>
    Task<RiskAssessment?> GetByIdAsync(Guid assessmentId, CancellationToken cancellationToken);

    /// <summary>Reads one page of an exam's flagged assessments, highest score first, with their signals, for display only.</summary>
    /// <param name="examId">The exam.</param>
    /// <param name="filter">Which decisions to include.</param>
    /// <param name="page">The page to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<RiskFlagPage> ListFlagsAsync(Guid examId, RiskFlagFilter filter, PageRequest page, CancellationToken cancellationToken);

    /// <summary>Whether the attempt has a flagged assessment that no reviewer has decided on.</summary>
    /// <param name="attemptId">The attempt.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> HasOpenFlagAsync(Guid attemptId, CancellationToken cancellationToken);
}
