using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;

/// <summary>The exam may not be deleted, because it is published or because something refers to it.</summary>
/// <param name="reason">Why, in words for the author to read.</param>
public sealed class ExamNotDeletableError(string reason) : DomainException($"This exam cannot be deleted. {reason}")
{
    /// <inheritdoc />
    public override string ErrorCode => "exam_not_deletable";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}
