namespace ExamPlatform.Modules.QuestionBank.Contracts;

/// <summary>
/// The topics each question is labelled with (FR-36, accuracy by topic): the labels the author gave it, such as "algebra". Other modules read
/// them through this Contracts project only, never through the bank's tables.
/// </summary>
/// <remarks>
/// Topics are labels, not content: the bank lets them change even when a question is in use, and they never reach a candidate or a score. So
/// they are read as they are now, not at the version an attempt sat, which is why they are not part of <see cref="QuestionSnapshot"/>.
/// </remarks>
public interface IQuestionTopics
{
    /// <summary>Finds the topics of each of the given questions.</summary>
    /// <param name="questionIds">The questions to look up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The topics by question id, in the bank's own form (lower case, as the author's labels were normalized). A question with no topic is absent
    /// from the result; ids that match no question are absent too.
    /// </returns>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> GetTopicsAsync(IReadOnlyCollection<Guid> questionIds, CancellationToken cancellationToken);
}
