using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;

/// <summary>The question is not part of the exam, or the option is not one of the question's options.</summary>
public sealed class InvalidAnswerError() : DomainException("That answer does not match a question and option of this exam.")
{
    /// <inheritdoc />
    public override string ErrorCode => "invalid_answer";

    /// <inheritdoc />
    public override int HttpStatusCode => 400;
}
