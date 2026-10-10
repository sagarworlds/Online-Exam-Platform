using ExamPlatform.Modules.Admin.Application.Dtos;
using ExamPlatform.Modules.Admin.Application.Ports;
using ExamPlatform.Modules.Admin.Domain;
using Microsoft.EntityFrameworkCore;

namespace ExamPlatform.Modules.Admin.Infrastructure;

/// <summary>EF Core-backed <see cref="IBrandingRepository"/>, over the single <see cref="InstituteBranding"/> row.</summary>
public sealed class BrandingRepository(AdminDbContext context) : IBrandingRepository
{
    /// <inheritdoc />
    public async Task<InstituteBranding?> FindForChangeAsync(CancellationToken cancellationToken) =>
        await context.InstituteBrandings.FirstOrDefaultAsync(b => b.Id == InstituteBranding.SingletonId, cancellationToken);

    /// <inheritdoc />
    public InstituteBranding Start(DateTime nowUtc)
    {
        var branding = InstituteBranding.Create(nowUtc);
        context.InstituteBrandings.Add(branding);
        return branding;
    }

    // The projection leaves the logo's bytes in the database: the candidate pages read the branding on every start, and the bytes are
    // only needed by the logo route.
    /// <inheritdoc />
    public async Task<BrandingDto?> FindSettingsAsync(CancellationToken cancellationToken) =>
        await context.InstituteBrandings
            .AsNoTracking()
            .Where(b => b.Id == InstituteBranding.SingletonId)
            .Select(b => new BrandingDto(b.InstituteName, b.PrimaryColour, b.Logo != null, b.UpdatedAtUtc))
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<BrandingLogoDto?> FindLogoAsync(CancellationToken cancellationToken) =>
        await context.InstituteBrandings
            .AsNoTracking()
            .Where(b => b.Id == InstituteBranding.SingletonId && b.Logo != null)
            .Select(b => new BrandingLogoDto(b.Logo!, b.LogoContentType!, b.UpdatedAtUtc))
            .FirstOrDefaultAsync(cancellationToken);
}
