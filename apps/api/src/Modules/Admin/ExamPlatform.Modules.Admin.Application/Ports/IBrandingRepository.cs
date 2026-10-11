using ExamPlatform.Modules.Admin.Application.Dtos;
using ExamPlatform.Modules.Admin.Domain;

namespace ExamPlatform.Modules.Admin.Application.Ports;

/// <summary>Persistence port for the institute's <see cref="InstituteBranding"/> record (FR-41).</summary>
public interface IBrandingRepository
{
    /// <summary>
    /// Finds the record for a change. It is tracked, so the change is saved by the unit of work. Null when no branding was ever saved.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<InstituteBranding?> FindForChangeAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Starts the record on first use and tracks it for insertion on the next save. A request that fails before that save leaves nothing
    /// behind, because the unit of work is never committed.
    /// </summary>
    /// <param name="nowUtc">The current instant.</param>
    /// <returns>The new, empty record.</returns>
    InstituteBranding Start(DateTime nowUtc);

    /// <summary>Reads the branding candidates see, without the logo's bytes.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The branding, or null when none was ever saved.</returns>
    Task<BrandingDto?> FindSettingsAsync(CancellationToken cancellationToken);

    /// <summary>Reads the logo and its media type.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The logo, or null when there is none.</returns>
    Task<BrandingLogoDto?> FindLogoAsync(CancellationToken cancellationToken);
}
