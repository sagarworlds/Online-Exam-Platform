using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.QuestionBank.Domain.Exceptions;

/// <summary>A book or chapter breaks one of the bank's rules (FR-5); the message says which.</summary>
/// <param name="message">What is wrong, safe to show to the author.</param>
public sealed class InvalidBookError(string message) : DomainException(message)
{
    /// <inheritdoc />
    public override string ErrorCode => "invalid_book";

    /// <inheritdoc />
    public override int HttpStatusCode => 400;
}
