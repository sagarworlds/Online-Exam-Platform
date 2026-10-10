using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Admin.Domain.Exceptions;

/// <summary>The institute's name or primary colour is not acceptable (FR-41).</summary>
/// <param name="message">What is wrong, in words a staff member can act on.</param>
public sealed class InvalidBrandingError(string message) : DomainException(message)
{
    /// <inheritdoc />
    public override string ErrorCode => "invalid_branding";

    /// <inheritdoc />
    public override int HttpStatusCode => 400;
}
