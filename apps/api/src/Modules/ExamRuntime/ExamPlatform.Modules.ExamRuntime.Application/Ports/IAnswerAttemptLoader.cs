using ExamPlatform.Modules.ExamRuntime.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Application.Ports;

/// <summary>
/// Loads an attempt for one answer being saved or cleared: the attempt itself, its paper, and only the saved answer and sequence number for
/// the question being written. That is everything such a write reads or changes, so the attempt's other collections (marks, times,
/// revisions, warnings and so on) are not loaded.
/// </summary>
/// <remarks>
/// The attempt is tracked, like the one <see cref="IAttemptRepository.GetByIdAsync"/> returns, but its answers and sequence numbers hold
/// the one question's only. Anything that needs them all (closing the attempt when its time has run out, for one) must load it in full.
/// </remarks>
public interface IAnswerAttemptLoader
{
    /// <summary>Loads an attempt, tracked, for a write to one question's answer.</summary>
    /// <param name="attemptId">The attempt.</param>
    /// <param name="questionId">The question the write is about.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The attempt, or <see langword="null"/> when there is none with that id.</returns>
    Task<Attempt?> LoadForAnswerAsync(Guid attemptId, Guid questionId, CancellationToken cancellationToken);
}
