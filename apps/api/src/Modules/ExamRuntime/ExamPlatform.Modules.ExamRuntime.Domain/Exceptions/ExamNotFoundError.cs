using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;

/// <summary>No exam has the given id. Said to staff, who may know exams exist; a candidate is told <see cref="ExamNotAvailableError"/> instead.</summary>
public sealed class ExamNotFoundError() : DomainException("No exam matches the given id.")
{
    /// <inheritdoc />
    public override string ErrorCode => "exam_not_found";

    /// <inheritdoc />
    public override int HttpStatusCode => 404;
}
