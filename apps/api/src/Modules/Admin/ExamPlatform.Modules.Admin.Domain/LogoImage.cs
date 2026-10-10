namespace ExamPlatform.Modules.Admin.Domain;

/// <summary>The image formats an institute logo may be (FR-41).</summary>
public enum LogoFormat
{
    /// <summary>PNG, the usual choice for a logo with transparent parts.</summary>
    Png,

    /// <summary>JPEG.</summary>
    Jpeg,

    /// <summary>WebP.</summary>
    Webp,
}

/// <summary>
/// The rules for an institute logo (FR-41): which image formats are accepted and how large the file may be. The format is read from the
/// file's leading bytes, never from its name or from the Content-Type the uploader sent, because either can say anything. SVG is not
/// accepted: it is markup that can carry script, and a logo does not need to be a vector image to be shown.
/// </summary>
public static class LogoImage
{
    /// <summary>
    /// The largest logo accepted, in bytes. The logo is stored in the database and fetched by every candidate's first page load, so a
    /// small limit keeps the candidate pages fast on a slow connection (FR-53).
    /// </summary>
    public const int MaxBytes = 256 * 1024;

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] RiffTag = "RIFF"u8.ToArray();
    private static readonly byte[] WebpTag = "WEBP"u8.ToArray();

    // A WebP file is a RIFF container whose form type, at offset 8, is "WEBP". Checking only "RIFF" would also accept WAV and AVI.
    private const int WebpFormTypeOffset = 8;
    private const int WebpMinimumLength = 12;

    /// <summary>Finds the format of an image from its leading bytes.</summary>
    /// <param name="content">The file's bytes.</param>
    /// <returns>The format, or null when the bytes are not one of the accepted formats.</returns>
    public static LogoFormat? Detect(ReadOnlySpan<byte> content)
    {
        if (content.StartsWith(PngSignature.AsSpan()))
        {
            return LogoFormat.Png;
        }

        if (content.StartsWith(JpegSignature.AsSpan()))
        {
            return LogoFormat.Jpeg;
        }

        if (content.Length >= WebpMinimumLength
            && content[..RiffTag.Length].SequenceEqual(RiffTag)
            && content.Slice(WebpFormTypeOffset, WebpTag.Length).SequenceEqual(WebpTag))
        {
            return LogoFormat.Webp;
        }

        return null;
    }

    /// <summary>The media type a logo of this format is served with.</summary>
    /// <param name="format">The logo's format.</param>
    /// <returns>The media type, such as <c>image/png</c>.</returns>
    public static string ContentTypeOf(LogoFormat format) => format switch
    {
        LogoFormat.Png => "image/png",
        LogoFormat.Jpeg => "image/jpeg",
        LogoFormat.Webp => "image/webp",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown logo format."),
    };
}
