using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.QuestionBank.Domain.Exceptions;

/// <summary>A question breaks one of the bank's rules (FR-5); the message says which.</summary>
/// <param name="message">What is wrong with the question, safe to show to its author.</param>
public sealed class InvalidQuestionError(string message) : DomainException(message)
{
    /// <inheritdoc />
    public override string ErrorCode => "invalid_question";

    /// <inheritdoc />
    public override int HttpStatusCode => 400;
}
