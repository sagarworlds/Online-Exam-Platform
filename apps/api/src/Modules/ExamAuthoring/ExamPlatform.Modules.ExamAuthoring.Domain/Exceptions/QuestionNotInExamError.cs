using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;

/// <summary>The section does not hold the question that was to be taken out of it.</summary>
/// <param name="questionId">The question-bank id that matched nothing.</param>
/// <param name="sectionId">The section that was searched.</param>
public sealed class QuestionNotInExamError(Guid questionId, Guid sectionId)
    : DomainException($"Question {questionId} is not in section {sectionId} of this exam.")
{
    /// <inheritdoc />
    public override string ErrorCode => "question_not_in_exam";

    /// <inheritdoc />
    public override int HttpStatusCode => 404;
}
