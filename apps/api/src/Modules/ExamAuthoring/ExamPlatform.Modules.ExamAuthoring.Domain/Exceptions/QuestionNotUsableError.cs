using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;

/// <summary>The question bank will not let this question into an exam: it is retired, or exams may only hold approved questions and it is not (FR-8).</summary>
/// <param name="questionId">The question.</param>
/// <param name="reason">Why, in the bank's words.</param>
public sealed class QuestionNotUsableError(Guid questionId, string reason) : DomainException($"Question {questionId} cannot be added to an exam. {reason}")
{
    /// <inheritdoc />
    public override string ErrorCode => "question_not_usable";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}
