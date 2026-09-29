using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Consent.Domain.Exceptions;

/// <summary>The consent record was already withdrawn; withdrawal is not idempotent so double-withdrawal is surfaced, not swallowed.</summary>
public sealed class ConsentAlreadyWithdrawnError() : DomainException("This consent has already been withdrawn.")
{
    /// <inheritdoc />
    public override string ErrorCode => "consent_already_withdrawn";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}
