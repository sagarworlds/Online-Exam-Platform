using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;

/// <summary>An accommodation cannot be set from the given values; the message says why.</summary>
/// <param name="message">What is wrong, safe to show to a caller.</param>
public sealed class InvalidAccommodationError(string message) : DomainException(message)
{
    /// <inheritdoc />
    public override string ErrorCode => "invalid_accommodation";

    /// <inheritdoc />
    public override int HttpStatusCode => 400;
}
