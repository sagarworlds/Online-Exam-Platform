using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Admin.Domain.Exceptions;

/// <summary>No logo has been set, so there is none to show (FR-41).</summary>
public sealed class LogoNotFoundError() : DomainException("No institute logo has been set.")
{
    /// <inheritdoc />
    public override string ErrorCode => "logo_not_found";

    /// <inheritdoc />
    public override int HttpStatusCode => 404;
}
