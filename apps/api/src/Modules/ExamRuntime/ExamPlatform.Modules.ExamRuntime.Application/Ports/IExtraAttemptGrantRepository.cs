using ExamPlatform.Modules.ExamRuntime.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Application.Ports;

/// <summary>Persistence port for <see cref="ExtraAttemptGrant"/>.</summary>
public interface IExtraAttemptGrantRepository
{
    /// <summary>Starts tracking a new grant; it is stored when the unit of work saves.</summary>
    /// <param name="grant">The grant to add.</param>
    void Add(ExtraAttemptGrant grant);

    /// <summary>How many extra attempts a candidate has been granted at an exam.</summary>
    /// <param name="examId">The exam.</param>
    /// <param name="candidateId">The candidate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<int> CountAsync(Guid examId, Guid candidateId, CancellationToken cancellationToken);

    /// <summary>How many extra attempts a candidate has been granted, per exam; exams with none are absent.</summary>
    /// <param name="candidateId">The candidate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyDictionary<Guid, int>> CountsForCandidateAsync(Guid candidateId, CancellationToken cancellationToken);

    /// <summary>How many extra attempts each candidate of an exam has been granted; candidates with none are absent.</summary>
    /// <param name="examId">The exam.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyDictionary<Guid, int>> CountsForExamAsync(Guid examId, CancellationToken cancellationToken);
}
