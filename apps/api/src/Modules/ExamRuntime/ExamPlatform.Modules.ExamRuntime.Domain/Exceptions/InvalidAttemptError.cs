using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;

/// <summary>An attempt cannot be created from the given values; the message says why.</summary>
/// <param name="message">What is wrong, safe to show to a caller.</param>
public sealed class InvalidAttemptError(string message) : DomainException(message)
{
    /// <inheritdoc />
    public override string ErrorCode => "invalid_attempt";

    /// <inheritdoc />
    public override int HttpStatusCode => 400;
}
