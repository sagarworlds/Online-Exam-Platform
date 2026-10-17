namespace ExamPlatform.SharedKernel.Application.Pdf;

/// <summary>
/// The built-in faces the PDF writer uses: Helvetica for reports and the Times faces for the certificate's lettering. All are among the
/// fourteen standard fonts every PDF reader has, so no font file is embedded or shipped.
/// </summary>
public enum PdfFont
{
    /// <summary>Helvetica, for body text and table cells.</summary>
    Regular,

    /// <summary>Helvetica-Bold, for headings and table headers.</summary>
    Bold,

    /// <summary>Times-Roman, for formal text.</summary>
    TimesRoman,

    /// <summary>Times-Bold, for formal headings and names.</summary>
    TimesBold,

    /// <summary>Times-Italic, for the connecting words of formal text.</summary>
    TimesItalic,
}

/// <summary>An RGB colour with each channel from 0 (none) to 1 (full), as PDF colour operators take them.</summary>
/// <param name="Red">The red channel, from 0 to 1.</param>
/// <param name="Green">The green channel, from 0 to 1.</param>
/// <param name="Blue">The blue channel, from 0 to 1.</param>
public readonly record struct PdfColor(double Red, double Green, double Blue)
{
    /// <summary>Black.</summary>
    public static PdfColor Black { get; } = new(0, 0, 0);

    /// <summary>A grey where 0 is black and 1 is white.</summary>
    /// <param name="level">The grey level, from 0 to 1.</param>
    /// <returns>The grey colour.</returns>
    public static PdfColor Gray(double level) => new(level, level, level);

    /// <summary>A colour from the red, green and blue bytes packed into one number, as web colours are written: 0xRRGGBB.</summary>
    /// <param name="rgb">The packed colour, for example 0x1B2A49.</param>
    /// <returns>The colour.</returns>
    public static PdfColor FromRgb(int rgb) => new(((rgb >> 16) & 0xFF) / 255.0, ((rgb >> 8) & 0xFF) / 255.0, (rgb & 0xFF) / 255.0);
}
