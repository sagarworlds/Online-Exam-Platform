using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Admin.Domain.Exceptions;

/// <summary>The file sent as a logo is not a PNG, JPEG or WebP image (FR-41).</summary>
public sealed class UnsupportedLogoTypeError() : DomainException("The logo must be a PNG, JPEG or WebP image.")
{
    /// <inheritdoc />
    public override string ErrorCode => "unsupported_logo_type";

    /// <inheritdoc />
    public override int HttpStatusCode => 415;
}
