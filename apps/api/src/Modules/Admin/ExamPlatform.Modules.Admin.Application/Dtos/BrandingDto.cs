namespace ExamPlatform.Modules.Admin.Application.Dtos;

/// <summary>The institute's branding as the candidate-facing pages use it (FR-41).</summary>
/// <param name="InstituteName">The name to show, or null for the platform's own.</param>
/// <param name="PrimaryColour">The hex colour to use, or null for the default colour.</param>
/// <param name="HasLogo">Whether there is a logo to show.</param>
/// <param name="UpdatedAtUtc">When the branding last changed; null when it was never saved.</param>
public sealed record BrandingDto(string? InstituteName, string? PrimaryColour, bool HasLogo, DateTime? UpdatedAtUtc)
{
    /// <summary>What the platform shows before anyone saves a branding: the default look.</summary>
    public static BrandingDto Unset { get; } = new(null, null, false, null);
}

/// <summary>An institute logo as it is served: its bytes, their media type, and when it was set.</summary>
/// <param name="Content">The image's bytes.</param>
/// <param name="ContentType">The media type, such as <c>image/png</c>.</param>
/// <param name="UpdatedAtUtc">When the logo was set; the basis of its entity tag.</param>
public sealed record BrandingLogoDto(byte[] Content, string ContentType, DateTime UpdatedAtUtc);
