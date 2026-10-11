namespace ExamPlatform.SharedKernel.Application.Pdf;

/// <summary>
/// The two built-in Helvetica faces the PDF writer uses. Both are among the fourteen standard fonts every PDF reader has, so no font file is
/// embedded or shipped.
/// </summary>
public enum PdfFont
{
    /// <summary>Helvetica, for body text and table cells.</summary>
    Regular,

    /// <summary>Helvetica-Bold, for headings and table headers.</summary>
    Bold,
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
}
