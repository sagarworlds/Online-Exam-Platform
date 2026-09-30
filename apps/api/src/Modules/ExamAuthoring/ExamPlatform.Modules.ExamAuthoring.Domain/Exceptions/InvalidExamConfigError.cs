using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;

public sealed class InvalidExamConfigError : DomainException
{
    public InvalidExamConfigError(string message) : base($"Invalid exam config: {message}") { }
}
