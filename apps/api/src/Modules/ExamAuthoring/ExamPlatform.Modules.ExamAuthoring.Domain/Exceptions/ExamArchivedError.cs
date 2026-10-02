using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;

/// <summary>The exam is archived, so nothing about it can be changed any more.</summary>
public sealed class ExamArchivedError() : DomainException("This exam is archived and can no longer be changed.")
{
    /// <inheritdoc />
    public override string ErrorCode => "exam_archived";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}
