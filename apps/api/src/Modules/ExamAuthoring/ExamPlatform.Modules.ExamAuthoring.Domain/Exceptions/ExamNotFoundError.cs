using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;

public sealed class ExamNotFoundError : DomainException
{
    public ExamNotFoundError(Guid examId) : base($"Exam with ID {examId} not found.") { }
}
