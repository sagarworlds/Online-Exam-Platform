using ExamPlatform.Modules.Admin.Application.Dtos;
using ExamPlatform.Modules.Admin.Application.Ports;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.Admin.Application.Commands;

/// <summary>Handles the removal of the institute's logo (FR-41).</summary>
public sealed class RemoveBrandingLogoHandler(
    IBrandingRepository repository,
    IAdminUnitOfWork unitOfWork,
    BrandingAudit audit,
    Clock clock)
{
    /// <summary>
    /// Removes the logo. It is idempotent: with no logo set there is nothing to remove, so nothing is saved or audited and the current
    /// branding is returned as it stands.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The branding as it now is.</returns>
    public async Task<BrandingDto> HandleAsync(CancellationToken cancellationToken)
    {
        var branding = await repository.FindForChangeAsync(cancellationToken);
        if (branding is null)
        {
            return BrandingDto.Unset;
        }

        if (!branding.HasLogo)
        {
            return BrandingMapping.ToDto(branding);
        }

        branding.RemoveLogo(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync("Branding.LogoRemoved", new Dictionary<string, string>(), cancellationToken);

        return BrandingMapping.ToDto(branding);
    }
}
