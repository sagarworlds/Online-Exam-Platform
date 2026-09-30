using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;

public sealed class DuplicateQuestionError : DomainException
{
    public DuplicateQuestionError(Guid questionId, Guid sectionId)
        : base($"Question {questionId} already exists in section {sectionId}.") { }
}
