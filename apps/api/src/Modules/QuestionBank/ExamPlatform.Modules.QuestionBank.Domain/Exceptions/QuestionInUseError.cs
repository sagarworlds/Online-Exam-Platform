using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.QuestionBank.Domain.Exceptions;

/// <summary>
/// The question is part of an exam or has been answered by candidates, so it cannot be deleted: exams read their questions
/// live from the bank and a candidate's result points at it.
/// </summary>
/// <param name="message">Why the question is in use, safe to show to its author.</param>
public sealed class QuestionInUseError(string message) : DomainException(message)
{
    /// <inheritdoc />
    public override string ErrorCode => "question_in_use";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}
