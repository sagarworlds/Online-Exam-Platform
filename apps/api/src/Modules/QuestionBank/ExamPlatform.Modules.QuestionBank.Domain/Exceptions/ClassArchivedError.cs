using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.QuestionBank.Domain.Exceptions;

/// <summary>
/// The class is archived: it is kept for the books already under it, but no book can be put under it until it is restored.
/// </summary>
/// <param name="message">Says which class is archived and what can be done about it.</param>
public sealed class ClassArchivedError(string message) : DomainException(message)
{
    /// <inheritdoc />
    public override string ErrorCode => "class_archived";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}
