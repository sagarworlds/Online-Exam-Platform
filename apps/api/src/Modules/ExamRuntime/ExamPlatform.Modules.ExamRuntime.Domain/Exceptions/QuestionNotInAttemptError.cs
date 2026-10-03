using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;

/// <summary>A candidate acted on a question (cleared its answer, marked it) that is not part of the exam they are sitting.</summary>
public sealed class QuestionNotInAttemptError() : DomainException("That question is not part of this exam.")
{
    /// <inheritdoc />
    public override string ErrorCode => "question_not_in_attempt";

    /// <inheritdoc />
    public override int HttpStatusCode => 404;
}
