using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using ExamPlatform.SharedKernel.Application.Pdf;

namespace ExamPlatform.SharedKernel.UnitTests.Pdf;

/// <summary>
/// The embedded Devanagari font in a PDF (FR-38): the objects that describe it, the glyphs a line is drawn with, and the ToUnicode map that lets a
/// reader copy the text back in order.
/// </summary>
public class PdfDevanagariFontTests
{
    private static string Text(byte[] pdf) => Encoding.Latin1.GetString(pdf);

    private static byte[] DocumentWith(string line)
    {
        var document = new PdfDocument("Devanagari");
        document.AddPage().Text(line, PdfFont.Regular, 10, 72, 700);
        return document.Build();
    }

    /// <summary>The glyphs drawn on the page, in drawing order, each with its ID and the origin it was drawn at.</summary>
    private static List<(int GlyphId, double X, double Y)> DrawnGlyphs(string pdf) =>
        Regex.Matches(pdf, @"1 0 0 1 (-?[\d.]+) (-?[\d.]+) Tm <([0-9A-F]{4})> Tj")
            .Select(m => (
                Convert.ToInt32(m.Groups[3].Value, 16),
                double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)))
            .ToList();

    /// <summary>The ToUnicode map: each glyph ID to the text it copies as.</summary>
    private static Dictionary<int, string> ToUnicodeOf(string pdf)
    {
        var start = pdf.IndexOf("endcodespacerange", StringComparison.Ordinal);
        var end = pdf.IndexOf("endcmap", StringComparison.Ordinal);
        var map = new Dictionary<int, string>();
        foreach (Match m in Regex.Matches(pdf[start..end], @"<([0-9A-F]{4})> <([0-9A-F]+)>"))
            map[Convert.ToInt32(m.Groups[1].Value, 16)] = Encoding.BigEndianUnicode.GetString(Convert.FromHexString(m.Groups[2].Value));

        return map;
    }

    /// <summary>The font file as the PDF stores it, inflated back to the bytes it was taken from.</summary>
    private static byte[] EmbeddedFontFile(byte[] pdf)
    {
        var match = Regex.Match(Text(pdf), @"<< /Length (\d+) /Length1 (\d+) /Filter /FlateDecode >>\nstream\n");
        Assert.True(match.Success, "the font file's stream is missing");

        var start = match.Index + match.Length;
        var compressed = pdf.AsSpan(start, int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)).ToArray();
        using var input = new MemoryStream(compressed);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        Assert.Equal(int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture), (int)output.Length);
        return output.ToArray();
    }

    /// <summary>The CIDToGIDMap as the PDF stores it, inflated: the font glyph each CID draws, indexed by CID.</summary>
    private static ushort[] CidToGidOf(byte[] pdf)
    {
        var text = Text(pdf);
        var reference = Regex.Match(text, @"/CIDToGIDMap (\d+) 0 R");
        Assert.True(reference.Success, "the CIDToGIDMap is not referenced");

        var header = Regex.Match(text, $@"\n{reference.Groups[1].Value} 0 obj\n<< /Length (\d+) /Filter /FlateDecode >>\nstream\n");
        Assert.True(header.Success, "the CIDToGIDMap stream is missing");
        var compressed = pdf.AsSpan(header.Index + header.Length, int.Parse(header.Groups[1].Value, CultureInfo.InvariantCulture)).ToArray();

        using var input = new MemoryStream(compressed);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);

        var bytes = output.ToArray();
        var map = new ushort[bytes.Length / 2];
        for (var i = 0; i < map.Length; i++)
            map[i] = (ushort)((bytes[2 * i] << 8) | bytes[2 * i + 1]);

        return map;
    }

    [Fact]
    public void ADocumentWithDevanagari_EmbedsTheFontAsAType0FontOverACidFontType2()
    {
        var text = Text(DocumentWith("कैसे"));

        Assert.Contains("/Type /Font /Subtype /Type0", text, StringComparison.Ordinal);
        Assert.Contains("/Subtype /CIDFontType2", text, StringComparison.Ordinal);
        Assert.Contains("/Encoding /Identity-H", text, StringComparison.Ordinal);
        Assert.Matches(@"/CIDToGIDMap \d+ 0 R", text);
        Assert.Contains("/FontFile2 ", text, StringComparison.Ordinal);
        Assert.Contains("/ToUnicode ", text, StringComparison.Ordinal);
        // One page: 4 and 5 are the Helvetica faces, 6 the content, 7 the information, so the Devanagari font is 8 and its descendant 9.
        Assert.Contains("/Font << /F1 4 0 R /F2 5 0 R /F3 8 0 R >>", text, StringComparison.Ordinal);
        Assert.Contains("/DescendantFonts [9 0 R]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ALatinOnlyDocument_HasNoDevanagariFont_AndItsObjectsAreAsBefore()
    {
        var document = new PdfDocument("Latin");
        document.AddPage().Text("Asha Kumar", PdfFont.Regular, 10, 72, 700);

        var text = Text(document.Build());

        Assert.DoesNotContain("Type0", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Identity-H", text, StringComparison.Ordinal);
        Assert.DoesNotContain("/F3", text, StringComparison.Ordinal);
        Assert.Contains("/Resources << /Font << /F1 4 0 R /F2 5 0 R >> >>", text, StringComparison.Ordinal);
        Assert.Contains("/Info 7 0 R", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFontFile_IsTheWholeEmbeddedFont_CompressedWithFlate()
    {
        var file = EmbeddedFontFile(DocumentWith("कैसे"));

        Assert.Equal(ReferenceShaping.FontBytes(), file);
    }

    [Theory]
    [InlineData("कैसे")]
    [InlineData("क्षत्रिय")]
    [InlineData("ज्ञान और विज्ञान")]
    public void TheDrawnGlyphs_AreTheGlyphsAndPlacesHarfBuzzGivesTheText(string word)
    {
        const double size = 10;
        const double originX = 72;
        const double baseline = 700;
        var bytes = DocumentWith(word);
        var glyphOfCid = CidToGidOf(bytes);

        // Each CID is looked up in the CIDToGIDMap, so the drawn glyph is the font glyph HarfBuzz gave the character.
        var drawn = DrawnGlyphs(Text(bytes)).Select(glyph => (GlyphId: (int)glyphOfCid[glyph.GlyphId], glyph.X, glyph.Y)).ToList();
        var expected = ReferenceShaping.Shape(word)
            .Select(glyph => ((int)glyph.GlyphId, X: originX + glyph.X * size / 1000, Y: baseline + glyph.Y * size / 1000))
            .OrderBy(glyph => glyph.Item1).ThenBy(glyph => glyph.X).ToList();
        var actual = drawn.OrderBy(glyph => glyph.GlyphId).ThenBy(glyph => glyph.X).ToList();

        // The writer draws the glyphs in character order, not in the order HarfBuzz lays them out, so the two are compared as sets of glyph and
        // place. Positions are written to a hundredth of a point.
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].Item1, actual[i].GlyphId);
            Assert.True(Math.Abs(expected[i].X - actual[i].X) < 0.006, $"glyph {expected[i].Item1} is at x {actual[i].X}, expected {expected[i].X}");
            Assert.True(Math.Abs(expected[i].Y - actual[i].Y) < 0.006, $"glyph {expected[i].Item1} is at y {actual[i].Y}, expected {expected[i].Y}");
        }
    }

    [Theory]
    [InlineData("कैसे")]
    [InlineData("क्षत्रिय")]
    [InlineData("कि नी")]
    [InlineData("ज्ञान और विज्ञान")]
    public void ToUnicode_ReturnsTheOriginalText_WhenTheGlyphsAreCopiedInOrder(string word)
    {
        var pdf = Text(DocumentWith(word));
        var map = ToUnicodeOf(pdf);

        var copied = string.Concat(DrawnGlyphs(pdf).Select(glyph => map.GetValueOrDefault(glyph.GlyphId, string.Empty)));

        Assert.Equal(word, copied);
    }

    [Fact]
    public void TheWidthArray_GivesEachDrawnGlyphItsFontAdvance()
    {
        var bytes = DocumentWith("क्षत्रिय");
        var pdf = Text(bytes);
        var glyphOfCid = CidToGidOf(bytes);
        var widths = Regex.Match(pdf, @"/W \[(.*?)\] /CIDToGIDMap");
        Assert.True(widths.Success, "the width array is missing");

        var advances = Regex.Matches(widths.Groups[1].Value, @"(\d+) \[(\d+)\]")
            .ToDictionary(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), m => int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));

        foreach (var glyph in DrawnGlyphs(pdf))
            Assert.Equal(ReferenceShaping.GlyphAdvance(glyphOfCid[glyph.GlyphId]), advances[glyph.GlyphId]);
    }

    [Fact]
    public void ALatinRun_BesideDevanagari_IsSetInHelvetica_AndPlacedAfterIt()
    {
        var text = Text(DocumentWith("Asha अशा Kumar"));

        Assert.Contains("BT /F1 10 Tf 72 700 Td (Asha ) Tj ET", text, StringComparison.Ordinal);
        var kumar = Regex.Match(text, @"BT /F1 10 Tf ([\d.]+) 700 Td \( Kumar\) Tj ET");
        Assert.True(kumar.Success, "the Latin run after the Devanagari is missing");

        var expectedX = 72 + PdfText.Width("Asha ", PdfFont.Regular, 10) + ReferenceShaping.Width("अशा") * 10 / 1000.0;
        Assert.Equal(expectedX, double.Parse(kumar.Groups[1].Value, CultureInfo.InvariantCulture), precision: 1);
    }

    [Fact]
    public void ADevanagariRun_IsWrappedInASpan_WhoseActualTextIsItsOwnCharacters()
    {
        // The span lets a reader copy or search the run as written, whatever order its glyphs were drawn in. अशा is U+0905, U+0936, U+093E.
        var text = Text(DocumentWith("अशा"));

        Assert.Contains("/Span << /ActualText <FEFF09050936093E> >> BDC BT /F3 10 Tf ", text, StringComparison.Ordinal);
        Assert.Contains(" ET EMC\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ATitleWithDevanagari_IsWrittenAsUtf16_SoItSurvivesTheFile()
    {
        var document = new PdfDocument("अशा");
        document.AddPage();

        var text = Text(document.Build());

        // अ is U+0905, श is U+0936 and ा is U+093E.
        Assert.Contains("/Title <FEFF09050936093E>", text, StringComparison.Ordinal);
    }
}
