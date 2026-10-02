using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.QuestionBank.Domain.Exceptions;

/// <summary>No question has the requested id.</summary>
public sealed class QuestionNotFoundError() : DomainException("No question matches the given id.")
{
    /// <inheritdoc />
    public override string ErrorCode => "question_not_found";

    /// <inheritdoc />
    public override int HttpStatusCode => 404;
}
