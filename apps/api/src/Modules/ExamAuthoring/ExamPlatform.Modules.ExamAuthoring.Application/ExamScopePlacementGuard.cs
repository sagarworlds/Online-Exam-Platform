using ExamPlatform.Modules.ExamAuthoring.Application.Ports;
using ExamPlatform.Modules.ExamAuthoring.Domain;
using ExamPlatform.Modules.QuestionBank.Contracts;

namespace ExamPlatform.Modules.ExamAuthoring.Application;

/// <summary>
/// Objects when moving a question would leave a draft exam holding a question outside the book or chapters it is limited to
/// (FR-11). The scope rule is checked when a question is added and when the scope changes, so without this a move could slip
/// a question out from under a scope and the exam's next scope change would be refused for a reason the author never caused.
/// </summary>
public sealed class ExamScopePlacementGuard(IExamRepository repository) : IQuestionPlacementGuard
{
    /// <inheritdoc />
    /// <remarks>
    /// Only drafts are checked. A published exam's scope is fixed and exam delivery never reads where a question is filed, so
    /// a move cannot hurt it; refusing would only make a published exam block the bank for good.
    /// </remarks>
    public async Task<IReadOnlyList<PlacementObjection>> CheckAsync(
        IReadOnlyCollection<Guid> questionIds, Guid bookId, Guid chapterId, CancellationToken cancellationToken)
    {
        var uses = await repository.ListUsesOfQuestionsAsync(questionIds, cancellationToken);
        var draftIds = uses.Where(u => u.Status == ExamStatus.Draft).Select(u => u.ExamId).Distinct().ToList();
        if (draftIds.Count == 0)
            return [];

        var destination = new QuestionPlacement(bookId, chapterId);
        var outside = (await repository.ListByIdsAsync(draftIds, cancellationToken))
            .Where(exam => !exam.Scope.Allows(destination))
            .ToDictionary(exam => exam.Id);

        return uses
            .Where(u => u.Status == ExamStatus.Draft && outside.ContainsKey(u.ExamId))
            .Select(u => new PlacementObjection(
                u.QuestionId,
                $"The draft exam \"{u.ExamName}\" only takes questions from the book or chapters it is limited to, and this chapter is not one of them."))
            .ToList();
    }
}
