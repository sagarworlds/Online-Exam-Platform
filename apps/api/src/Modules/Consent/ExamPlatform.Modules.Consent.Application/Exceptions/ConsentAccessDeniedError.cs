using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Consent.Application.Exceptions;

/// <summary>
/// A signed-in person tried to read, grant or withdraw consent that belongs to someone else (FR-44).
/// Consent is personal: the person it is about is the only one who may act on it. A guardian cannot act for a
/// minor yet, because nothing binds a guardian record to a guardian's own account (see the Guardian endpoints).
/// </summary>
public sealed class ConsentAccessDeniedError() : DomainException("A person may only read or change their own consent.")
{
    /// <inheritdoc />
    public override string ErrorCode => "consent_access_denied";

    /// <inheritdoc />
    public override int HttpStatusCode => 403;
}
