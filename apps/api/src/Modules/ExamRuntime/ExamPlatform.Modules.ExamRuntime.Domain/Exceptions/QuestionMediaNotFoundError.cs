using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;

/// <summary>The question has no picture with that key: the key is not one the attempt was given, or the question has changed since.</summary>
public sealed class QuestionMediaNotFoundError() : DomainException("That question has no such picture.")
{
    /// <inheritdoc />
    public override string ErrorCode => "question_media_not_found";

    /// <inheritdoc />
    public override int HttpStatusCode => 404;
}
