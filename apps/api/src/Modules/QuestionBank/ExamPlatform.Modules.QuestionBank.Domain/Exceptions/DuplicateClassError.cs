using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.QuestionBank.Domain.Exceptions;

/// <summary>Another class already has that name (names are compared ignoring case).</summary>
/// <param name="name">The name that is taken.</param>
public sealed class DuplicateClassError(string name) : DomainException($"There is already a class called \"{name}\".")
{
    /// <inheritdoc />
    public override string ErrorCode => "duplicate_class";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}
