using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;

/// <summary>The exam's window, or its late-entry cutoff, has passed, so nobody can start any more.</summary>
public sealed class ExamClosedError() : DomainException("This exam is closed; it can no longer be started.")
{
    /// <inheritdoc />
    public override string ErrorCode => "exam_closed";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}
