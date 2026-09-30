using ExamPlatform.Modules.ExamAuthoring.Domain;

namespace ExamPlatform.Modules.ExamAuthoring.Application.Ports;

/// Repository port for Exam aggregate.
public interface IExamRepository
{
    /// Add a new exam aggregate to the repository.
    void Add(Exam exam);

    /// Get an exam by its ID, or null if not found.
    Task<Exam?> GetByIdAsync(Guid examId, CancellationToken cancellationToken = default);

    /// Get an exam by its ID, or throw ExamNotFoundError if not found.
    Task<Exam> GetByIdOrThrowAsync(Guid examId, CancellationToken cancellationToken = default);

    /// List all exams for a series, excluding soft-deleted ones.
    Task<IReadOnlyList<Exam>> ListBySeriesAsync(Guid seriesId, CancellationToken cancellationToken = default);

    /// Update an existing exam aggregate.
    void Update(Exam exam);
}
