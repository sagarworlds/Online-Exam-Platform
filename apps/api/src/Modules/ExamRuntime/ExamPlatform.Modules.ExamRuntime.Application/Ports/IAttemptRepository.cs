using ExamPlatform.Modules.ExamRuntime.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Application.Ports;

/// <summary>One answer in a finished attempt.</summary>
/// <param name="SelectedOptionIds">The options the candidate chose.</param>
/// <param name="VersionNumber">The version of the question the attempt sat, or null for an attempt made before versions were recorded.</param>
/// <param name="AnswerText">What the candidate typed, for a text question; null for an answer made by choosing options.</param>
public sealed record SubmittedAnswer(IReadOnlyCollection<Guid> SelectedOptionIds, int? VersionNumber, string? AnswerText = null);

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

    /// <summary>Lists the answers given to a question in finished attempts that still count, for its statistics (FR-9).</summary>
    /// <param name="questionId">The question-bank id of the question.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One entry per answer; attempts still open, and attempts an administrator invalidated, are left out.</returns>
    Task<IReadOnlyList<SubmittedAnswer>> ListSubmittedAnswersAsync(Guid questionId, CancellationToken cancellationToken);

    /// <summary>Lists every attempt a candidate has made, without their answers, for read-only display.</summary>
    /// <param name="candidateId">The candidate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<Attempt>> ListForCandidateAsync(Guid candidateId, CancellationToken cancellationToken);

    /// <summary>
    /// Loads every submitted attempt that included a question, whether the candidate answered it or it was only on
    /// their drawn paper, tracked with their answers and paper so each can be rescored. For
    /// <see cref="ExamPlatform.Modules.QuestionBank.Contracts.IAttemptRescorer"/>: an open attempt is excluded,
    /// since it has no score yet to revise.
    /// </summary>
    /// <param name="questionId">The question.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<Attempt>> ListSubmittedByQuestionIdAsync(Guid questionId, CancellationToken cancellationToken);
}
