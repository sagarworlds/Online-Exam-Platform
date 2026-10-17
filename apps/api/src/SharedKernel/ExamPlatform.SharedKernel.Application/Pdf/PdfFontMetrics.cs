namespace ExamPlatform.SharedKernel.Application.Pdf;

/// <summary>
/// The advance widths of the Helvetica and Times faces, from the standard AFM metrics, so a line can be measured before it is drawn, wrapped to
/// fit its column or centred on the page. Only printable ASCII has a width here; the Latin-1 letters above it use a typical width, which is
/// close enough for wrapping.
/// </summary>
/// <remarks>
/// A width is in thousandths of an em. The tables run from the space (32) to the tilde (126), 95 entries each. Measuring with the real
/// widths matters: a fixed guess per character either leaves cells half empty or lets text run out of its column.
/// </remarks>
internal static class PdfFontMetrics
{
    /// <summary>The width used for a Latin-1 letter outside printable ASCII in a Helvetica face, in thousandths of an em.</summary>
    private const int TypicalHelveticaWidth = 556;

    /// <summary>The width used for a Latin-1 letter outside printable ASCII in a Times face, in thousandths of an em.</summary>
    private const int TypicalTimesWidth = 500;

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

    private static readonly int[] TimesRomanWidths =
    [
        250, 333, 408, 500, 500, 833, 778, 180, 333, 333, 500, 564, 250, 333, 250, 278, // 32-47: space to slash
        500, 500, 500, 500, 500, 500, 500, 500, 500, 500,                               // 48-57: digits
        278, 278, 564, 564, 564, 444, 921,                                              // 58-64: colon to at
        722, 667, 667, 722, 611, 556, 722, 722, 333, 389, 722, 611, 889,                // 65-77: A to M
        722, 722, 556, 722, 667, 556, 611, 722, 722, 944, 722, 722, 611,                // 78-90: N to Z
        333, 278, 333, 469, 500, 333,                                                   // 91-96: brackets to grave
        444, 500, 444, 500, 444, 333, 500, 500, 278, 278, 500, 278, 778, 500, 500, 500, // 97-112: a to p
        500, 333, 389, 278, 500, 500, 722, 500, 500, 444,                               // 113-122: q to z
        480, 200, 480, 541,                                                             // 123-126: braces, bar, tilde
    ];

    private static readonly int[] TimesBoldWidths =
    [
        250, 333, 555, 500, 500, 1000, 833, 278, 333, 333, 500, 570, 250, 333, 250, 278, // 32-47
        500, 500, 500, 500, 500, 500, 500, 500, 500, 500,                               // 48-57
        333, 333, 570, 570, 570, 500, 930,                                              // 58-64
        722, 667, 722, 722, 667, 611, 778, 778, 389, 500, 778, 667, 944,                // 65-77
        722, 778, 611, 778, 722, 556, 667, 722, 722, 1000, 722, 722, 667,               // 78-90
        333, 278, 333, 581, 500, 333,                                                   // 91-96
        500, 556, 444, 556, 444, 333, 500, 556, 278, 333, 556, 278, 833, 556, 500, 556, // 97-112
        556, 444, 389, 333, 556, 500, 722, 500, 500, 444,                               // 113-122
        394, 220, 394, 520,                                                             // 123-126
    ];

    private static readonly int[] TimesItalicWidths =
    [
        250, 333, 420, 500, 500, 833, 778, 214, 333, 333, 500, 675, 250, 333, 250, 278, // 32-47
        500, 500, 500, 500, 500, 500, 500, 500, 500, 500,                               // 48-57
        333, 333, 675, 675, 675, 500, 920,                                              // 58-64
        611, 611, 667, 722, 611, 611, 722, 722, 333, 444, 667, 556, 833,                // 65-77
        667, 722, 611, 722, 611, 500, 556, 722, 611, 833, 611, 556, 556,                // 78-90
        389, 278, 389, 422, 500, 333,                                                   // 91-96
        500, 500, 444, 500, 444, 278, 500, 500, 278, 278, 444, 278, 722, 500, 500, 500, // 97-112
        500, 389, 389, 278, 500, 444, 667, 444, 444, 389,                               // 113-122
        400, 275, 400, 541,                                                             // 123-126
    ];

    /// <summary>The advance width of one character, in thousandths of an em.</summary>
    /// <param name="character">A character the PDF can show (see <see cref="PdfText.CanShow"/>).</param>
    /// <param name="font">The face the character is set in.</param>
    /// <returns>The width.</returns>
    public static int WidthOf(char character, PdfFont font)
    {
        var (table, typical) = font switch
        {
            PdfFont.Bold => (BoldWidths, TypicalHelveticaWidth),
            PdfFont.TimesRoman => (TimesRomanWidths, TypicalTimesWidth),
            PdfFont.TimesBold => (TimesBoldWidths, TypicalTimesWidth),
            PdfFont.TimesItalic => (TimesItalicWidths, TypicalTimesWidth),
            _ => (RegularWidths, TypicalHelveticaWidth),
        };

        return character is >= ' ' and <= '~' ? table[character - ' '] : typical;
    }
}
