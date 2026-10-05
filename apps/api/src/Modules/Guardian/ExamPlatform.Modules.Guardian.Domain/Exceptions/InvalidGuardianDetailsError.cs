using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Guardian.Domain.Exceptions;

/// <summary>A guardian's e-mail address or name is not usable (400).</summary>
public sealed class InvalidGuardianDetailsError(string message) : DomainException(message)
{
    /// <inheritdoc />
    public override string ErrorCode => "invalid_guardian";

    /// <inheritdoc />
    public override int HttpStatusCode => 400;
}
