using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Admin.Domain.Exceptions;

/// <summary>The logo file is bigger than the platform accepts (FR-41).</summary>
/// <param name="maxBytes">The largest size accepted, in bytes.</param>
public sealed class LogoTooLargeError(int maxBytes)
    : DomainException($"The logo must be at most {maxBytes / 1024} KB.")
{
    /// <inheritdoc />
    public override string ErrorCode => "logo_too_large";

    /// <inheritdoc />
    public override int HttpStatusCode => 413;
}
