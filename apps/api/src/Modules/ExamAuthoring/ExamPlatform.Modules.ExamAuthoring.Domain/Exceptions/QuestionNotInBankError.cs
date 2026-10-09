using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;

/// <summary>An exam was asked to include a question the question bank does not have.</summary>
/// <param name="questionId">The id that matched no question.</param>
public sealed class QuestionNotInBankError(Guid questionId) : DomainException($"Question {questionId} does not exist in the question bank.")
{
    /// <inheritdoc />
    public override string ErrorCode => "question_not_found";

    /// <inheritdoc />
    public override int HttpStatusCode => 404;
}
