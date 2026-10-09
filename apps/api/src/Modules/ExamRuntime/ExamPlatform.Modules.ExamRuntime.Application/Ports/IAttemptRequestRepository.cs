using ExamPlatform.Modules.ExamRuntime.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Application.Ports;

/// <summary>Persistence port for <see cref="AttemptRequest"/>.</summary>
public interface IAttemptRequestRepository
{
    /// <summary>Starts tracking a new request; it is stored when the unit of work saves.</summary>
    /// <param name="request">The request to add.</param>
    void Add(AttemptRequest request);

    /// <summary>Loads one request, tracked so a decision on it is saved.</summary>
    /// <param name="requestId">The request's id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The request, or <see langword="null"/> when none has that id.</returns>
    Task<AttemptRequest?> GetByIdAsync(Guid requestId, CancellationToken cancellationToken);

    /// <summary>Whether the candidate has a request for the exam that is still waiting.</summary>
    /// <param name="examId">The exam.</param>
    /// <param name="candidateId">The candidate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> HasPendingAsync(Guid examId, Guid candidateId, CancellationToken cancellationToken);

    /// <summary>Lists requests with the given status, oldest first, so the longest-waiting is at the top of a queue.</summary>
    /// <param name="status">Which requests to list.</param>
    /// <param name="take">How many to return at most.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<AttemptRequest>> ListAsync(AttemptRequestStatus status, int take, CancellationToken cancellationToken);

    /// <summary>The most recent request a candidate made at each exam; exams they never asked about are absent.</summary>
    /// <param name="candidateId">The candidate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyDictionary<Guid, AttemptRequest>> LatestForCandidateAsync(Guid candidateId, CancellationToken cancellationToken);
}
