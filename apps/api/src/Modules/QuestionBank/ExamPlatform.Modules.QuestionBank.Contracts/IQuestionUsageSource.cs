namespace ExamPlatform.Modules.QuestionBank.Contracts;

/// <summary>
/// Something outside the bank that can say where questions are in use. The bank must not depend on the modules that use its
/// questions, so each of them contributes one of these (ADR 0001): the bank asks every registered source before it lets a
/// question be deleted or its answer key be changed, and a module that starts using questions in a new way adds a source
/// without the bank changing.
/// </summary>
public interface IQuestionUsageSource
{
    /// <summary>Finds the uses this source knows of for the given questions.</summary>
    /// <param name="questionIds">The questions to look up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One entry per use; a question this source does not use has no entry.</returns>
    Task<IReadOnlyList<QuestionUse>> FindAsync(IReadOnlyCollection<Guid> questionIds, CancellationToken cancellationToken);
}
