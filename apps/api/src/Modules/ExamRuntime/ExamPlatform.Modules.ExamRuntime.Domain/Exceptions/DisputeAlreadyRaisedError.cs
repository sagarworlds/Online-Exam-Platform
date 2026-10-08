using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;

/// <summary>The candidate already disputed this question in this attempt; staff's answer to it is final.</summary>
public sealed class DisputeAlreadyRaisedError()
    : DomainException("You have already disputed this question. Staff's answer to it is final; it appears under your result.")
{
    /// <inheritdoc />
    public override string ErrorCode => "dispute_already_raised";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}
