using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;

/// <summary>The exam was published, so it can no longer be edited.</summary>
public sealed class ExamNotDraftError() : DomainException("Only a draft exam can be changed; this one is already published.")
{
    /// <inheritdoc />
    public override string ErrorCode => "exam_not_draft";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}
