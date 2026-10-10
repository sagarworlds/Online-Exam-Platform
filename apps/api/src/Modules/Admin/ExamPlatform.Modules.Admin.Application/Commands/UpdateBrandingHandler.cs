using ExamPlatform.Modules.Admin.Application.Dtos;
using ExamPlatform.Modules.Admin.Application.Ports;
using ExamPlatform.Modules.Admin.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.Admin.Application.Commands;

/// <summary>Handles <see cref="UpdateBrandingCommand"/>: sets the institute's name and primary colour (FR-41).</summary>
public sealed class UpdateBrandingHandler(
    IBrandingRepository repository,
    IAdminUnitOfWork unitOfWork,
    BrandingAudit audit,
    Clock clock)
{
    /// <summary>Saves the name and colour, starting the branding record on first use.</summary>
    /// <param name="command">The new name and colour; blank values clear them.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The branding as it now is.</returns>
    /// <exception cref="InvalidBrandingError">The name is too long, or the colour is not a six-digit hex colour. Nothing is saved.</exception>
    public async Task<BrandingDto> HandleAsync(UpdateBrandingCommand command, CancellationToken cancellationToken)
    {
        var branding = await repository.FindForChangeAsync(cancellationToken) ?? repository.Start(clock.UtcNow);
        branding.Describe(command.InstituteName, command.PrimaryColour, clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(
            "Branding.Updated",
            new Dictionary<string, string>
            {
                ["instituteName"] = branding.InstituteName ?? string.Empty,
                ["primaryColour"] = branding.PrimaryColour ?? string.Empty,
            },
            cancellationToken);

        return BrandingMapping.ToDto(branding);
    }
}
