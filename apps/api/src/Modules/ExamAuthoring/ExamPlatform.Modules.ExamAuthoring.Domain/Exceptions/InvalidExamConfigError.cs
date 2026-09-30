using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;

public sealed class InvalidExamConfigError(string message) : DomainException($"Invalid exam config: {message}")
{
    public override string ErrorCode => "invalid_exam_config";
    public override int HttpStatusCode => 400;
}
