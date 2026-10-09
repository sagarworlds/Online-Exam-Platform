using ExamPlatform.Modules.ExamAuthoring.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Contracts;

namespace ExamPlatform.Modules.ExamAuthoring.Application;

/// <summary>
/// Tells the question bank which exams hold a question. Exams read their questions live from the bank, so a question
/// that is in an exam must not be deleted from under it.
/// </summary>
public sealed class ExamQuestionUsageSource(IExamRepository repository) : IQuestionUsageSource
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<QuestionUse>> FindAsync(IReadOnlyCollection<Guid> questionIds, CancellationToken cancellationToken)
    {
        if (questionIds.Count == 0)
            return [];

        var uses = await repository.ListUsesOfQuestionsAsync(questionIds, cancellationToken);
        return uses.Select(u => new QuestionUse(u.QuestionId, QuestionUseKind.InExam, u.ExamName)).ToList();
    }
}
