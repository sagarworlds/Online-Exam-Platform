using ExamPlatform.Modules.ExamRuntime.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Application.Ports;

/// <summary>Persistence port for <see cref="Accommodation"/> (FR-49).</summary>
public interface IAccommodationRepository
{
    /// <summary>Starts tracking a new accommodation; it is stored when the unit of work saves.</summary>
    /// <param name="accommodation">The accommodation to add.</param>
    void Add(Accommodation accommodation);

    /// <summary>Marks an accommodation for deletion; it is removed when the unit of work saves.</summary>
    /// <param name="accommodation">The accommodation to remove.</param>
    void Remove(Accommodation accommodation);

    /// <summary>Loads a candidate's accommodation at an exam so it can be changed.</summary>
    /// <param name="examId">The exam.</param>
    /// <param name="candidateId">The candidate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The accommodation, or <see langword="null"/> when they have none.</returns>
    Task<Accommodation?> FindAsync(Guid examId, Guid candidateId, CancellationToken cancellationToken);

    /// <summary>The accommodations of an exam's candidates, by candidate; candidates with none are absent.</summary>
    /// <param name="examId">The exam.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyDictionary<Guid, Accommodation>> ListForExamAsync(Guid examId, CancellationToken cancellationToken);

    /// <summary>A candidate's accommodations, by exam; exams where they have none are absent.</summary>
    /// <param name="candidateId">The candidate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyDictionary<Guid, Accommodation>> ListForCandidateAsync(Guid candidateId, CancellationToken cancellationToken);
}
