using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;

public sealed class DuplicateQuestionError(Guid questionId, Guid sectionId)
    : DomainException($"Question {questionId} already exists in section {sectionId}.")
{
    public override string ErrorCode => "duplicate_question";
    public override int HttpStatusCode => 409;
}
