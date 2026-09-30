using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;

public sealed class ExamNotFoundError(Guid examId) : DomainException($"Exam with ID {examId} not found.")
{
    public override string ErrorCode => "exam_not_found";
    public override int HttpStatusCode => 404;
}
