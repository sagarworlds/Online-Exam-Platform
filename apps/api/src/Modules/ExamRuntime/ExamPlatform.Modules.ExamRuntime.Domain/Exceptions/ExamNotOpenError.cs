using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;

/// <summary>The exam's window has not opened yet.</summary>
public sealed class ExamNotOpenError() : DomainException("This exam has not opened yet.")
{
    /// <inheritdoc />
    public override string ErrorCode => "exam_not_open";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}
