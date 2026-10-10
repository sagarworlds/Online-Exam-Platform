using System.Text.RegularExpressions;
using ExamPlatform.Modules.Admin.Domain.Exceptions;
using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Admin.Domain;

/// <summary>
/// The institute's branding for the candidate-facing pages (FR-41): its name, its primary colour and its logo. There is one record for the
/// whole platform. Until someone sets a field it is empty, and the platform keeps its default look.
/// </summary>
public sealed class InstituteBranding : Entity
{
    /// <summary>
    /// The id of the one branding record. A fixed id rather than a generated one, so the record is always found the same way and a second
    /// one cannot be created by accident.
    /// </summary>
    public static readonly Guid SingletonId = new("0b4d0000-0000-4000-8000-000000000041");

    /// <summary>The longest institute name shown to candidates.</summary>
    public const int MaxNameLength = 100;

    private static readonly Regex ColourPattern = new("^#[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant);

    private InstituteBranding() : base(SingletonId)
    {
    }

    /// <summary>The institute's name as candidates see it, or null when it is not set.</summary>
    public string? InstituteName { get; private set; }

    /// <summary>The primary colour as an upper-case hex code such as <c>#1A56DB</c>, or null for the default colour.</summary>
    public string? PrimaryColour { get; private set; }

    /// <summary>The logo's bytes, or null when there is none. Kept in the database rather than on disk, so every replica serves the same logo.</summary>
    public byte[]? Logo { get; private set; }

    /// <summary>The logo's media type, found from its bytes when it was set; null when there is no logo.</summary>
    public string? LogoContentType { get; private set; }

    /// <summary>When the branding last changed. It is also the logo's entity tag, so a browser fetches a changed logo again.</summary>
    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>Whether a logo is set.</summary>
    public bool HasLogo => Logo is not null;

    /// <summary>Starts an empty branding record, to be filled in and saved.</summary>
    /// <param name="nowUtc">The current instant.</param>
    /// <returns>A record with no name, colour or logo.</returns>
    public static InstituteBranding Create(DateTime nowUtc) => new() { UpdatedAtUtc = nowUtc };

    /// <summary>Sets the institute's name and primary colour. A blank value clears that field back to the default.</summary>
    /// <param name="instituteName">The name candidates should see; blank for none.</param>
    /// <param name="primaryColour">A six-digit hex colour such as <c>#1A56DB</c>; blank for the default colour.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <exception cref="InvalidBrandingError">The name is longer than <see cref="MaxNameLength"/>, or the colour is not a six-digit hex colour.</exception>
    public void Describe(string? instituteName, string? primaryColour, DateTime nowUtc)
    {
        var name = string.IsNullOrWhiteSpace(instituteName) ? null : instituteName.Trim();
        if (name?.Length > MaxNameLength)
        {
            throw new InvalidBrandingError($"The institute name must be at most {MaxNameLength} characters.");
        }

        // Only the six-digit form is accepted. A shorthand such as #1AD is read by some browsers and not by others, so the stored value
        // is always one that renders the same everywhere, and a colour name is refused rather than silently falling back to the default.
        var colour = string.IsNullOrWhiteSpace(primaryColour) ? null : primaryColour.Trim();
        if (colour is not null && !ColourPattern.IsMatch(colour))
        {
            throw new InvalidBrandingError("The primary colour must be a six-digit hex colour such as #1A56DB.");
        }

        InstituteName = name;
        PrimaryColour = colour?.ToUpperInvariant();
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Replaces the logo. The format is read from the bytes, not from anything the uploader claimed.</summary>
    /// <param name="content">The image's bytes.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <exception cref="LogoTooLargeError">The file is bigger than <see cref="LogoImage.MaxBytes"/>.</exception>
    /// <exception cref="UnsupportedLogoTypeError">The bytes are not a PNG, JPEG or WebP image.</exception>
    public void SetLogo(byte[] content, DateTime nowUtc)
    {
        // The size is checked first: it is the cheaper test, and it keeps a huge file from being scanned for a format at all.
        if (content.Length > LogoImage.MaxBytes)
        {
            throw new LogoTooLargeError(LogoImage.MaxBytes);
        }

        var format = LogoImage.Detect(content) ?? throw new UnsupportedLogoTypeError();
        Logo = content;
        LogoContentType = LogoImage.ContentTypeOf(format);
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Removes the logo. Removing one that is not set changes nothing.</summary>
    /// <param name="nowUtc">The current instant.</param>
    public void RemoveLogo(DateTime nowUtc)
    {
        if (Logo is null)
        {
            return;
        }

        Logo = null;
        LogoContentType = null;
        UpdatedAtUtc = nowUtc;
    }
}
