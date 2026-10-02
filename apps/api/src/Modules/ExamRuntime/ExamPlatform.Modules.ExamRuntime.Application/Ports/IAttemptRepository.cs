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

    /// <summary>Loads every attempt a candidate has made at an exam, oldest first, with their answers, tracked so changes to them are saved.</summary>
    /// <param name="examId">The exam.</param>
    /// <param name="candidateId">The candidate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Their attempts in order of <see cref="Attempt.Number"/>; empty when they have not started the exam.</returns>
    Task<IReadOnlyList<Attempt>> ListForCandidateAtExamAsync(Guid examId, Guid candidateId, CancellationToken cancellationToken);

    /// <summary>Lists every attempt anyone has made at an exam, without their answers, for read-only display.</summary>
    /// <param name="examId">The exam.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<Attempt>> ListForExamAsync(Guid examId, CancellationToken cancellationToken);

    /// <summary>Finds which of the given questions a candidate has saved an answer to, in any attempt.</summary>
    /// <param name="questionIds">The question-bank ids to look for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The ids among <paramref name="questionIds"/> that have at least one saved answer.</returns>
    Task<IReadOnlyCollection<Guid>> FindAnsweredQuestionIdsAsync(IReadOnlyCollection<Guid> questionIds, CancellationToken cancellationToken);

    /// <summary>Lists every attempt a candidate has made, without their answers, for read-only display.</summary>
    /// <param name="candidateId">The candidate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<Attempt>> ListForCandidateAsync(Guid candidateId, CancellationToken cancellationToken);
}
