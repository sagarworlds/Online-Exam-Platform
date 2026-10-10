using ExamPlatform.Modules.Admin.Application.Dtos;
using ExamPlatform.Modules.Admin.Domain;

namespace ExamPlatform.Modules.Admin.Application;

/// <summary>Maps the <see cref="InstituteBranding"/> record to the DTO the API returns, so every handler reports it the same way.</summary>
public static class BrandingMapping
{
    /// <summary>Maps a branding record, without its logo bytes.</summary>
    /// <param name="branding">The record to map.</param>
    /// <returns>The DTO for the record.</returns>
    public static BrandingDto ToDto(InstituteBranding branding) =>
        new(branding.InstituteName, branding.PrimaryColour, branding.HasLogo, branding.UpdatedAtUtc);
}
