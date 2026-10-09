using ExamPlatform.Modules.ExamRuntime.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Application.Ports;

/// <summary>Persistence port for <see cref="Dispute"/>.</summary>
public interface IDisputeRepository
{
    /// <summary>Starts tracking a new dispute; it is stored when the unit of work saves.</summary>
    /// <param name="dispute">The dispute to add.</param>
    void Add(Dispute dispute);

    /// <summary>Loads one dispute, tracked so settling it is saved.</summary>
    /// <param name="disputeId">The dispute's id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The dispute, or <see langword="null"/> when none has that id.</returns>
    Task<Dispute?> GetByIdAsync(Guid disputeId, CancellationToken cancellationToken);

    /// <summary>Whether a dispute of this question in this attempt exists, open or settled.</summary>
    /// <param name="attemptId">The attempt.</param>
    /// <param name="questionId">The question.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> ExistsAsync(Guid attemptId, Guid questionId, CancellationToken cancellationToken);

    /// <summary>Lists the disputes raised in one attempt, oldest first, for read-only display.</summary>
    /// <param name="attemptId">The attempt.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<Dispute>> ListForAttemptAsync(Guid attemptId, CancellationToken cancellationToken);

    /// <summary>Lists disputes with the given status, oldest first, so the longest-waiting is at the top of a queue.</summary>
    /// <param name="status">Which disputes to list.</param>
    /// <param name="take">How many to return at most.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<Dispute>> ListAsync(DisputeStatus status, int take, CancellationToken cancellationToken);

    /// <summary>Loads every open dispute about a question, tracked, so a key correction can settle them together.</summary>
    /// <param name="questionId">The question.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<Dispute>> ListOpenForQuestionAsync(Guid questionId, CancellationToken cancellationToken);
}
