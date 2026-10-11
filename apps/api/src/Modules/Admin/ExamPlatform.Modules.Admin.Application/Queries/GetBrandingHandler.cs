using ExamPlatform.Modules.Admin.Application.Dtos;
using ExamPlatform.Modules.Admin.Application.Ports;

namespace ExamPlatform.Modules.Admin.Application.Queries;

/// <summary>
/// Handles the read of the institute's branding (FR-41). It is served to everyone, signed in or not, because the sign-in and registration
/// pages are candidate-facing too and must already show the institute's look.
/// </summary>
public sealed class GetBrandingHandler(IBrandingRepository repository)
{
    /// <summary>Reads the branding the candidate pages use.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The branding, or the default look when none was ever saved.</returns>
    public async Task<BrandingDto> HandleAsync(CancellationToken cancellationToken) =>
        await repository.FindSettingsAsync(cancellationToken) ?? BrandingDto.Unset;
}
