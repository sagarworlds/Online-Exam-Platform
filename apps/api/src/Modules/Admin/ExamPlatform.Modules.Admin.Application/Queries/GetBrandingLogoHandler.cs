using ExamPlatform.Modules.Admin.Application.Dtos;
using ExamPlatform.Modules.Admin.Application.Ports;
using ExamPlatform.Modules.Admin.Domain.Exceptions;

namespace ExamPlatform.Modules.Admin.Application.Queries;

/// <summary>Handles the read of the institute's logo (FR-41). Served to everyone, for the same reason as <see cref="GetBrandingHandler"/>.</summary>
public sealed class GetBrandingLogoHandler(IBrandingRepository repository)
{
    /// <summary>Reads the logo.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The logo's bytes and media type.</returns>
    /// <exception cref="LogoNotFoundError">No logo has been set.</exception>
    public async Task<BrandingLogoDto> HandleAsync(CancellationToken cancellationToken) =>
        await repository.FindLogoAsync(cancellationToken) ?? throw new LogoNotFoundError();
}
