namespace ExamPlatform.Modules.ExamAuthoring.Contracts;

/// <summary>
/// What other modules may ask the exam builder (ADR 0001): Invite checks that an exam exists before inviting
/// anyone to it, and exam delivery reads a published exam's schedule and questions. Consumed through this
/// Contracts project only, never through the exam module's Domain, Application or Infrastructure.
/// </summary>
public interface IExamCatalog
{
    /// <summary>Reads one exam in any state, or null when none has that id.</summary>
    /// <param name="examId">The exam's id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ExamSnapshot?> FindAsync(Guid examId, CancellationToken cancellationToken);

    /// <summary>Reads the published exams among the given ids; unknown and unpublished ids are left out.</summary>
    /// <param name="examIds">The exams to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<ExamSnapshot>> FindPublishedAsync(IReadOnlyCollection<Guid> examIds, CancellationToken cancellationToken);
}
