using ExamPlatform.Modules.ExamRuntime.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Application.Ports;

/// <summary>Persistence port for <see cref="Attempt"/>.</summary>
public interface IAttemptRepository
{
    /// <summary>Starts tracking a new attempt; it is stored when the unit of work saves.</summary>
    /// <param name="attempt">The attempt to add.</param>
    void Add(Attempt attempt);

    /// <summary>Loads one attempt with its answers, tracked so changes to it are saved.</summary>
    /// <param name="attemptId">The attempt's id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The attempt, or <see langword="null"/> when none has that id.</returns>
    Task<Attempt?> GetByIdAsync(Guid attemptId, CancellationToken cancellationToken);

    /// <summary>Loads a candidate's attempt at an exam with its answers, tracked so changes to it are saved.</summary>
    /// <param name="examId">The exam.</param>
    /// <param name="candidateId">The candidate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The attempt, or <see langword="null"/> when the candidate has not started the exam.</returns>
    Task<Attempt?> FindAsync(Guid examId, Guid candidateId, CancellationToken cancellationToken);

    /// <summary>Lists every attempt a candidate has made, without their answers, for read-only display.</summary>
    /// <param name="candidateId">The candidate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<Attempt>> ListForCandidateAsync(Guid candidateId, CancellationToken cancellationToken);
}
