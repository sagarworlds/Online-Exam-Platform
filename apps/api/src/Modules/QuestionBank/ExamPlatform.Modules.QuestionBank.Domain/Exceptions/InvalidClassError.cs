using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.QuestionBank.Domain.Exceptions;

/// <summary>A class breaks one of the bank's rules; the message says which.</summary>
/// <param name="message">What is wrong, safe to show to the author.</param>
public sealed class InvalidClassError(string message) : DomainException(message)
{
    /// <inheritdoc />
    public override string ErrorCode => "invalid_class";

    /// <inheritdoc />
    public override int HttpStatusCode => 400;
}
