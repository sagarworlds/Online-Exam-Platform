using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;

/// <summary>The exam does not exist, is not published, or the candidate is not enrolled in it (the three are not told apart).</summary>
public sealed class ExamNotAvailableError() : DomainException("This exam is not available to you.")
{
    /// <inheritdoc />
    public override string ErrorCode => "exam_not_available";

    /// <inheritdoc />
    public override int HttpStatusCode => 404;
}
