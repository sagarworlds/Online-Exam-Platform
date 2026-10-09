using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;

/// <summary>Disputes are not being taken for this result: switched off, or the time allowed after the result was released has passed.</summary>
/// <param name="message">What is wrong, safe to show to the candidate.</param>
public sealed class DisputeWindowClosedError(string message) : DomainException(message)
{
    /// <inheritdoc />
    public override string ErrorCode => "dispute_window_closed";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}
