using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;

/// <summary>A question of a published exam cannot be read from the question bank; the exam cannot be shown or scored until that is fixed.</summary>
public sealed class ExamContentUnavailableError() : DomainException("The exam's questions could not be loaded.")
{
    /// <inheritdoc />
    public override string ErrorCode => "exam_content_unavailable";

    /// <inheritdoc />
    public override int HttpStatusCode => 500;
}
