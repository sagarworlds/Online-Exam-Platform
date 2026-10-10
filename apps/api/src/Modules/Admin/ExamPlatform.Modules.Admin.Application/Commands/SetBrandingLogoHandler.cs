using System.Globalization;
using ExamPlatform.Modules.Admin.Application.Dtos;
using ExamPlatform.Modules.Admin.Application.Ports;
using ExamPlatform.Modules.Admin.Domain;
using ExamPlatform.Modules.Admin.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.Admin.Application.Commands;

/// <summary>Handles <see cref="SetBrandingLogoCommand"/>: replaces the institute's logo (FR-41).</summary>
public sealed class SetBrandingLogoHandler(
    IBrandingRepository repository,
    IAdminUnitOfWork unitOfWork,
    BrandingAudit audit,
    Clock clock)
{
    /// <summary>Stores the logo in the database, starting the branding record on first use.</summary>
    /// <param name="command">The uploaded image.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The branding as it now is.</returns>
    /// <exception cref="LogoTooLargeError">The file is bigger than the accepted size. Nothing is saved.</exception>
    /// <exception cref="UnsupportedLogoTypeError">The file is not a PNG, JPEG or WebP image. Nothing is saved.</exception>
    public async Task<BrandingDto> HandleAsync(SetBrandingLogoCommand command, CancellationToken cancellationToken)
    {
        var branding = await repository.FindForChangeAsync(cancellationToken) ?? repository.Start(clock.UtcNow);
        branding.SetLogo(command.Content, clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(
            "Branding.LogoChanged",
            new Dictionary<string, string>
            {
                ["contentType"] = branding.LogoContentType ?? string.Empty,
                ["bytes"] = command.Content.Length.ToString(CultureInfo.InvariantCulture),
            },
            cancellationToken);

        return BrandingMapping.ToDto(branding);
    }
}
