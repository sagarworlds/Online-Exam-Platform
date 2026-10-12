namespace ExamPlatform.SharedKernel.Application.Pdf;

/// <summary>
/// The advance widths of the Helvetica faces, from the standard AFM metrics, so a line can be measured before it is drawn and wrapped to fit
/// its column. Only printable ASCII has a width here; the Latin-1 letters above it use a typical width, which is close enough for wrapping.
/// </summary>
/// <remarks>
/// A width is in thousandths of an em. The tables run from the space (32) to the tilde (126), 95 entries each. Measuring with the real
/// widths matters: a fixed guess per character either leaves cells half empty or lets text run out of its column.
/// </remarks>
internal static class PdfFontMetrics
{
    /// <summary>The width used for a Latin-1 letter outside printable ASCII, in thousandths of an em.</summary>
    private const int TypicalWidth = 556;

    private static readonly int[] RegularWidths =
    [
        278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278, // 32-47: space to slash
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556,                               // 48-57: digits
        278, 278, 584, 584, 584, 556, 1015,                                             // 58-64: colon to at
        667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833,                // 65-77: A to M
        722, 778, 667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611,                // 78-90: N to Z
        278, 278, 278, 469, 556, 333,                                                   // 91-96: brackets to grave
        556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556, 556, // 97-112: a to p
        556, 333, 500, 278, 556, 500, 722, 500, 500, 500,                               // 113-122: q to z
        334, 260, 334, 584,                                                             // 123-126: braces, bar, tilde
    ];

    private static readonly int[] BoldWidths =
    [
        278, 333, 474, 556, 556, 889, 722, 238, 333, 333, 389, 584, 278, 333, 278, 278, // 32-47
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556,                               // 48-57
        333, 333, 584, 584, 584, 611, 975,                                              // 58-64
        722, 722, 722, 722, 667, 611, 778, 722, 278, 556, 722, 611, 833,                // 65-77
        722, 778, 667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611,                // 78-90
        333, 278, 333, 584, 556, 333,                                                   // 91-96
        556, 611, 556, 611, 556, 333, 611, 611, 278, 278, 556, 278, 889, 611, 611, 611, // 97-112
        611, 389, 556, 333, 611, 556, 778, 556, 556, 500,                               // 113-122
        389, 280, 389, 584,                                                             // 123-126
    ];

    /// <summary>The advance width of one character, in thousandths of an em.</summary>
    /// <param name="character">A character the PDF can show (see <see cref="PdfText.CanShow"/>).</param>
    /// <param name="font">The face the character is set in.</param>
    /// <returns>The width.</returns>
    public static int WidthOf(char character, PdfFont font)
    {
        var table = font == PdfFont.Bold ? BoldWidths : RegularWidths;
        return character is >= ' ' and <= '~' ? table[character - ' '] : TypicalWidth;
    }
}
