using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.QuestionBank.Domain.Exceptions;

/// <summary>A review step that the question's status does not allow, such as approving a draft or retiring a question twice (FR-8).</summary>
/// <param name="message">What cannot be done and why, safe to show to whoever tried.</param>
public sealed class InvalidQuestionStatusError(string message) : DomainException(message)
{
    /// <inheritdoc />
    public override string ErrorCode => "invalid_question_status";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}
