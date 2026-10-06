using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.QuestionBank.Contracts;

namespace ExamPlatform.Modules.ExamRuntime.Application;

/// <summary>
/// How an attempt reads its questions from the bank (FR-7): at the version it was sitting, so an edit made after it started
/// cannot change what the candidate saw, how it is marked or what its review shows. The one place that rule lives.
/// </summary>
public static class AttemptQuestionReads
{
    /// <summary>Reads the questions of an attempt, each at the version the attempt recorded for it.</summary>
    /// <remarks>
    /// An attempt with no recorded versions (made before they were kept, or a preview, which is never stored) reads the questions as
    /// they are now, which is all that was ever possible for it.
    /// </remarks>
    /// <param name="questionBank">The bank.</param>
    /// <param name="attempt">The attempt whose questions are wanted.</param>
    /// <param name="questionIds">The questions to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The questions found by id.</returns>
    public static async Task<Dictionary<Guid, QuestionSnapshot>> ReadAsync(
        this IQuestionBank questionBank, Attempt attempt, IReadOnlyCollection<Guid> questionIds, CancellationToken cancellationToken)
    {
        var found = attempt.QuestionVersions.Count == 0
            ? await questionBank.GetAsync(questionIds, cancellationToken)
            : await questionBank.GetVersionsAsync(
                questionIds.Distinct().Select(id => new QuestionVersionRef(id, attempt.QuestionVersionOf(id))).ToList(), cancellationToken);

        return found.ToDictionary(q => q.Id);
    }
}
