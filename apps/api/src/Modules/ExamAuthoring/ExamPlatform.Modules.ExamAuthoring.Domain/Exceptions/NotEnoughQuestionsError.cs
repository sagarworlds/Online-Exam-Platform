using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;

/// <summary>A draw asked for more questions than the bank has that match and are not already in the exam.</summary>
/// <param name="requested">How many were asked for.</param>
/// <param name="available">How many could have been drawn.</param>
public sealed class NotEnoughQuestionsError(int requested, int available)
    : DomainException($"{requested} questions were asked for but only {available} match and are not already in this exam.")
{
    /// <inheritdoc />
    public override string ErrorCode => "not_enough_questions";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}
