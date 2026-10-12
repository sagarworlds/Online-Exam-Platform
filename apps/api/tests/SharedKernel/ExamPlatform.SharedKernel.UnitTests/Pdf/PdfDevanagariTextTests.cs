using System.Globalization;
using ExamPlatform.SharedKernel.Application.Pdf;
using HarfBuzzSharp;
using HbBuffer = HarfBuzzSharp.Buffer;
using HbFont = HarfBuzzSharp.Font;

namespace ExamPlatform.SharedKernel.UnitTests.Pdf;

/// <summary>
/// Devanagari text in the PDF writer (FR-38, FR-51): it is measured from its shaped glyphs and wrapped with those widths, so a line never runs
/// past its column, and a line break never starts with a vowel sign.
/// </summary>
public class PdfDevanagariTextTests
{
    private const string FontResource = "ExamPlatform.SharedKernel.Application.Pdf.Fonts.NotoSansDevanagari-Regular.ttf";

    // The same font file, shaped by HarfBuzz directly. The writer's measurements are checked against it, not against its own output.
    private static readonly Lazy<HbFont> ReferenceFont = new(() =>
    {
        using var stream = typeof(PdfText).Assembly.GetManifestResourceStream(FontResource)
            ?? throw new InvalidOperationException("The embedded font is missing.");
        using var blob = Blob.FromStream(stream);
        var face = new Face(blob, 0);
        var font = new HbFont(face);
        font.SetScale(1000, 1000);
        return font;
    });

    /// <summary>The width of text shaped directly by HarfBuzz, in points at the given size.</summary>
    private static double ShapedPoints(string text, double size)
    {
        using var buffer = new HbBuffer();
        buffer.AddUtf16(text);
        buffer.Direction = Direction.LeftToRight;
        buffer.Script = Script.Devanagari;
        ReferenceFont.Value.Shape(buffer, []);
        return buffer.GlyphPositions.Sum(position => position.XAdvance) * size / 1000.0;
    }

    private static bool IsMark(char c) =>
        CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;

    [Theory]
    [InlineData("कैसे")]
    [InlineData("क्षत्रिय")]
    [InlineData("भारत की राजधानी कौन सी है")]
    public void Width_OfDevanagari_IsTheShapedAdvance(string text) =>
        Assert.Equal(ShapedPoints(text, 9.5), PdfText.Width(text, PdfFont.Regular, 9.5), precision: 9);

    [Fact]
    public void Width_OfAPhrase_ShapesTheSpaceBetweenItsWords_WithTheFont()
    {
        // A space between two Devanagari words belongs to the Devanagari run, so the phrase is shaped as one.
        Assert.Equal(ShapedPoints("क्या है", 10), PdfText.Width("क्या है", PdfFont.Regular, 10), precision: 9);
    }

    [Fact]
    public void Width_OfMixedText_AddsTheLatinPartAndTheDevanagariPart()
    {
        var expected = PdfText.Width("Asha ", PdfFont.Regular, 10) + ShapedPoints("अशा", 10) + PdfText.Width(" Kumar", PdfFont.Regular, 10);

        Assert.Equal(expected, PdfText.Width("Asha अशा Kumar", PdfFont.Regular, 10), precision: 9);
    }

    [Fact]
    public void Wrap_OfDevanagari_FillsEachLineToTheShapedWidth()
    {
        const string text = "ज्ञान और विज्ञान के बीच संबंध समझाइए और बताइए कि त्रिभुज का क्षेत्रफल कैसे निकाला जाता है।";
        const double size = 8.5;
        const double maxWidth = 130;

        var lines = PdfText.Wrap(text, PdfFont.Regular, size, maxWidth);

        // The expected lines are the greedy fill of the words, each measured with HarfBuzz directly.
        var expected = new List<string>();
        var line = string.Empty;
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var joined = line.Length == 0 ? word : line + " " + word;
            if (line.Length == 0 || ShapedPoints(joined, size) <= maxWidth)
            {
                line = joined;
                continue;
            }

            expected.Add(line);
            line = word;
        }

        if (line.Length > 0)
            expected.Add(line);

        Assert.Equal(expected, lines);
        Assert.All(lines, l => Assert.True(ShapedPoints(l, size) <= maxWidth, $"'{l}' is wider than the column"));
    }

    [Fact]
    public void Wrap_OfALongDevanagariWord_NeverStartsALineWithAVowelSign()
    {
        // Each repeat has vowel signs; a break before one of them would leave it on a dotted circle at the start of a line.
        var word = string.Concat(Enumerable.Repeat("कुशलता", 6));

        var lines = PdfText.Wrap(word, PdfFont.Regular, 10, 40);

        Assert.True(lines.Count > 1, "the word should be broken over several lines");
        Assert.Equal(word, string.Concat(lines));
        Assert.All(lines, l => Assert.False(IsMark(l[0]), $"'{l}' starts with a mark"));
        Assert.All(lines, l => Assert.True(ShapedPoints(l, 10) <= 40, $"'{l}' is wider than the column"));
    }
}
