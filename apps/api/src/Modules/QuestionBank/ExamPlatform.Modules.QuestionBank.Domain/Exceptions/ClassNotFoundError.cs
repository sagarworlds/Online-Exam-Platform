using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.QuestionBank.Domain.Exceptions;

/// <summary>No class has the requested id.</summary>
public sealed class ClassNotFoundError() : DomainException("No class matches the given id.")
{
    /// <inheritdoc />
    public override string ErrorCode => "class_not_found";

    /// <inheritdoc />
    public override int HttpStatusCode => 404;
}
