using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Analytics.Domain.Exceptions;

/// <summary>The exam an analysis was asked for does not exist, so there is nothing to analyse.</summary>
public sealed class ExamNotFoundError() : DomainException("No exam with that id exists.")
{
    /// <inheritdoc />
    public override string ErrorCode => "exam_not_found";

    /// <inheritdoc />
    public override int HttpStatusCode => 404;
}
