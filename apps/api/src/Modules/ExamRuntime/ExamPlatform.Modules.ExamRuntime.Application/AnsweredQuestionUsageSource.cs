using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Contracts;

namespace ExamPlatform.Modules.ExamRuntime.Application;

/// <summary>
/// Tells the question bank which questions candidates have answered. A stored score was worked out against the answer key
/// as it was, and a review is worked out against the key as it is, so once a question has been answered its key must stay.
/// </summary>
public sealed class AnsweredQuestionUsageSource(IAttemptRepository attempts) : IQuestionUsageSource
{
    /// <summary>The description every answered question carries; nothing about the candidates is passed on.</summary>
    public const string Description = "Answered by candidates";

    /// <inheritdoc />
    public async Task<IReadOnlyList<QuestionUse>> FindAsync(IReadOnlyCollection<Guid> questionIds, CancellationToken cancellationToken)
    {
        if (questionIds.Count == 0)
            return [];

        var answered = await attempts.FindAnsweredQuestionIdsAsync(questionIds, cancellationToken);
        return answered.Select(id => new QuestionUse(id, QuestionUseKind.Answered, Description)).ToList();
    }
}
