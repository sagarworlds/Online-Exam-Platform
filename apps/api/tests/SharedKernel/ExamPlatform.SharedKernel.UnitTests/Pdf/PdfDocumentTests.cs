using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ExamPlatform.SharedKernel.Application.Pdf;

namespace ExamPlatform.SharedKernel.UnitTests.Pdf;

/// <summary>The PDF file itself: its header, its objects, its page tree and its cross-reference table (FR-34, FR-38).</summary>
public class PdfDocumentTests
{
    private static string Text(byte[] pdf) => Encoding.Latin1.GetString(pdf);

    /// <summary>A document of the given number of pages, each with one line of text, so the layout is easy to read back.</summary>
    private static byte[] DocumentOf(int pages, string title = "Test report")
    {
        var document = new PdfDocument(title);
        for (var i = 1; i <= pages; i++)
            document.AddPage().Text($"Page body {i}", PdfFont.Regular, 12, 72, 700);
        return document.Build();
    }

    [Fact]
    public void TheFile_StartsWithAPdfHeader_AndEndsWithTheEndOfFileMarker()
    {
        var text = Text(DocumentOf(1));

        Assert.StartsWith("%PDF-1.4\n", text, StringComparison.Ordinal);
        Assert.EndsWith("%%EOF\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSecondHeaderLine_HoldsBytesAboveSevenBitAscii_SoTransferToolsTreatTheFileAsBinary()
    {
        var pdf = DocumentOf(1);

        Assert.Equal((byte)'%', pdf[9]);
        Assert.True(pdf[10] > 0x7F && pdf[11] > 0x7F && pdf[12] > 0x7F && pdf[13] > 0x7F);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(12)]
    public void TheCrossReferenceTable_PointsAtEachObject_ByItsRealByteOffset(int pages)
    {
        var pdf = DocumentOf(pages);
        var text = Text(pdf);

        var startxref = int.Parse(text.Split("startxref\n")[1].Split('\n')[0], CultureInfo.InvariantCulture);
        Assert.Equal("xref", Encoding.Latin1.GetString(pdf, startxref, 4));

        // Objects are 1 catalogue, 2 page tree, one per page, two fonts, one content stream per page, and the information dictionary.
        var objectCount = 2 + pages + 2 + pages + 1;
        var entries = text[startxref..].Split('\n').Skip(3).Take(objectCount).ToList();
        Assert.Equal($"0 {objectCount + 1}", text[startxref..].Split('\n')[1]);
        for (var i = 1; i <= objectCount; i++)
        {
            var entry = entries[i - 1];
            Assert.Equal(20, entry.Length + 1);
            var offset = int.Parse(entry.Split(' ')[0], CultureInfo.InvariantCulture);
            Assert.StartsWith($"{i} 0 obj", Encoding.Latin1.GetString(pdf, offset, 12), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheOffsets_StayCorrect_WhenTheTextHasLatin1Letters()
    {
        // A letter such as é is one byte in Latin-1 but two in UTF-8; the offsets must count the byte the file holds.
        var document = new PdfDocument("Café");
        document.AddPage().Text("Résumé naïve", PdfFont.Regular, 12, 72, 700);
        var pdf = document.Build();
        var text = Text(pdf);

        var startxref = int.Parse(text.Split("startxref\n")[1].Split('\n')[0], CultureInfo.InvariantCulture);
        var offset = int.Parse(text[startxref..].Split('\n')[3].Split(' ')[0], CultureInfo.InvariantCulture);
        Assert.StartsWith("1 0 obj", Encoding.Latin1.GetString(pdf, offset, 7), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void ThePageTree_ListsEveryPage_AndCountsThem(int pages)
    {
        var text = Text(DocumentOf(pages));

        Assert.Contains($"/Count {pages} ", text, StringComparison.Ordinal);
        Assert.Equal(pages, Regex.Matches(text, @"/Type /Page\b").Count);
    }

    [Fact]
    public void EachPage_UsesItsOwnContentStream_AndTheSharedFonts()
    {
        // Two pages: 3 and 4 are the pages, 5 and 6 the fonts, 7 and 8 the contents, 9 the information.
        var text = Text(DocumentOf(2));

        Assert.Contains("/Font << /F1 5 0 R /F2 6 0 R >>", text, StringComparison.Ordinal);
        Assert.Contains("/Contents 7 0 R", text, StringComparison.Ordinal);
        Assert.Contains("/Contents 8 0 R", text, StringComparison.Ordinal);
        Assert.Contains("/Info 9 0 R", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheInformationDictionary_CarriesTheEscapedTitle()
    {
        var document = new PdfDocument(@"Report (A) \ B");
        document.AddPage();

        var text = Text(document.Build());

        Assert.Contains(@"/Title (Report \(A\) \\ B)", text, StringComparison.Ordinal);
        // One page: 1 catalogue, 2 page tree, 3 page, 4 and 5 fonts, 6 content, 7 information.
        Assert.Contains("/Info 7 0 R", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheText_IsPrintedAsWritten_InTheChosenFace()
    {
        var document = new PdfDocument("t");
        var page = document.AddPage();
        page.Text("Regular line", PdfFont.Regular, 10, 72, 700);
        page.Text("Bold line", PdfFont.Bold, 14, 72, 680);

        var text = Text(document.Build());

        Assert.Contains("BT /F1 10 Tf 72 700 Td (Regular line) Tj ET", text, StringComparison.Ordinal);
        Assert.Contains("BT /F2 14 Tf 72 680 Td (Bold line) Tj ET", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheShapes_AreDrawnWithTheirColours()
    {
        var document = new PdfDocument("shapes");
        var page = document.AddPage();
        page.StrokeRectangle(36, 36, 523, 770, 2, new PdfColor(0.15, 0.25, 0.45));
        page.FillRectangle(50, 100, 495, 20, PdfColor.Gray(0.9));
        page.Line(50, 90, 545, 90, 0.5, PdfColor.Gray(0.75));

        var text = Text(document.Build());

        Assert.Contains("q 0.15 0.25 0.45 RG 2 w 36 36 523 770 re S Q", text, StringComparison.Ordinal);
        Assert.Contains("q 0.9 0.9 0.9 rg 50 100 495 20 re f Q", text, StringComparison.Ordinal);
        Assert.Contains("q 0.75 0.75 0.75 RG 0.5 w 50 90 m 545 90 l S Q", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TextDrawnAfterAFill_IsNotPaintedInTheFillsColour()
    {
        // The fill is saved and restored around its box, so the fill colour cannot be the colour of the text that follows.
        var document = new PdfDocument("colour");
        var page = document.AddPage();
        page.FillRectangle(50, 100, 495, 20, PdfColor.Gray(0.9));
        page.Text("Header", PdfFont.Bold, 9, 50, 104);

        var text = Text(document.Build());

        Assert.Contains("q 0.9 0.9 0.9 rg 50 100 495 20 re f Q\nBT /F2 9 Tf 50 104 Td (Header) Tj ET", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ADocumentWithNoPages_IsRefused()
    {
        var document = new PdfDocument("empty");

        Assert.Throws<InvalidOperationException>(() => document.Build());
    }

    [Fact]
    public void ATitleTheFontCannotPrint_IsRefused()
    {
        // A Chinese character is neither Latin-1 nor Devanagari, so no font has a glyph for it.
        Assert.Throws<ArgumentException>(() => new PdfDocument("漢字"));
    }

    [Fact]
    public void TextTheFontCannotPrint_IsRefused_RatherThanDrawnAsABox()
    {
        var page = new PdfDocument("t").AddPage();

        Assert.Throws<ArgumentException>(() => page.Text("漢字", PdfFont.Regular, 10, 72, 700));
    }

    [Fact]
    public void PageCount_IsTheNumberOfPagesAdded()
    {
        var document = new PdfDocument("count");
        document.AddPage();
        document.AddPage();

        Assert.Equal(2, document.PageCount);
    }

    [Fact]
    public void ADocumentThatUsesNoTimesFace_NamesNone_AndIsWrittenAsItAlwaysWas()
    {
        var text = Text(DocumentOf(2));

        Assert.DoesNotContain("Times", text, StringComparison.Ordinal);
        Assert.DoesNotContain("/F4", text, StringComparison.Ordinal);
        // 1 catalogue, 2 page tree, 3 and 4 pages, 5 and 6 fonts, 7 and 8 contents, 9 information: nothing after it.
        Assert.Contains("/Size 10 ", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ATimesFace_IsWrittenAfterTheInformation_NamedInThePageResources_AndOnlyWhenUsed()
    {
        var document = new PdfDocument("t");
        var page = document.AddPage();
        page.Text("Roman line", PdfFont.TimesRoman, 12, 72, 700);
        page.Text("Italic line", PdfFont.TimesItalic, 12, 72, 680);

        var pdf = document.Build();
        var text = Text(pdf);

        // One page: 1 catalogue, 2 page tree, 3 page, 4 and 5 Helvetica, 6 content, 7 information, then the faces the page used, in order.
        Assert.Contains("/Info 7 0 R", text, StringComparison.Ordinal);
        Assert.Contains("/F4 8 0 R /F6 9 0 R", text, StringComparison.Ordinal);
        Assert.DoesNotContain("/F5", text, StringComparison.Ordinal);
        Assert.Contains("8 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Times-Roman /Encoding /WinAnsiEncoding >>", text, StringComparison.Ordinal);
        Assert.Contains("9 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Times-Italic /Encoding /WinAnsiEncoding >>", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Times-Bold", text, StringComparison.Ordinal);
        Assert.Contains("BT /F4 12 Tf 72 700 Td (Roman line) Tj ET", text, StringComparison.Ordinal);
        Assert.Contains("BT /F6 12 Tf 72 680 Td (Italic line) Tj ET", text, StringComparison.Ordinal);

        // The cross-reference table still points at every object, the new ones included.
        var startxref = int.Parse(text.Split("startxref\n")[1].Split('\n')[0], CultureInfo.InvariantCulture);
        var entries = text[startxref..].Split('\n').Skip(3).Take(9).ToList();
        for (var i = 1; i <= 9; i++)
        {
            var offset = int.Parse(entries[i - 1].Split(' ')[0], CultureInfo.InvariantCulture);
            Assert.StartsWith($"{i} 0 obj", Encoding.Latin1.GetString(pdf, offset, 12), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void APage_IsPortraitUnlessItAsksForLandscape()
    {
        var document = new PdfDocument("t");
        var portrait = document.AddPage();
        var landscape = document.AddPage(PdfPageOrientation.Landscape);

        var text = Text(document.Build());

        Assert.Equal((595, 842), (portrait.Width, portrait.Height));
        Assert.Equal((842, 595), (landscape.Width, landscape.Height));
        Assert.Contains("/MediaBox [0 0 595 842]", text, StringComparison.Ordinal);
        Assert.Contains("/MediaBox [0 0 842 595]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ColouredText_IsDrawnInItsColour_WithoutLeakingItToWhatFollows()
    {
        var document = new PdfDocument("t");
        var page = document.AddPage();
        page.Text("Gold", PdfFont.TimesBold, 20, 72, 700, new PdfColor(0.1, 0.2, 0.3));
        page.Text("Plain", PdfFont.Regular, 10, 72, 680);

        var text = Text(document.Build());

        Assert.Contains("q 0.1 0.2 0.3 rg 0 Tc\nBT /F5 20 Tf 72 700 Td (Gold) Tj ET\nQ\nBT /F1 10 Tf 72 680 Td (Plain) Tj ET", text, StringComparison.Ordinal);
    }

    [Fact]
    public void SpacedText_CarriesItsSpacing_AndSaysWhatItSpells()
    {
        var document = new PdfDocument("t");
        var page = document.AddPage();
        page.Text("AB", PdfFont.Bold, 10, 72, 700, PdfColor.Black, 3.5);

        var text = Text(document.Build());

        Assert.Contains("3.5 Tc", text, StringComparison.Ordinal);
        Assert.Contains("/Span << /ActualText <FEFF00410042> >> BDC", text, StringComparison.Ordinal);
        Assert.Contains("(AB) Tj ET\nEMC\nQ", text, StringComparison.Ordinal);
    }

    [Fact]
    public void CentredText_StartsHalfItsWidthToTheLeftOfTheCentre()
    {
        var document = new PdfDocument("t");
        var page = document.AddPage();

        // Hello in Times-Bold is 2278 units wide, so 22.78 points at size 10, and 11.39 of them sit left of the centre.
        page.TextCentered("Hello", PdfFont.TimesBold, 10, 100, 700, PdfColor.Black);

        Assert.Contains("BT /F5 10 Tf 88.61 700 Td (Hello) Tj ET", Text(document.Build()), StringComparison.Ordinal);
    }

    [Fact]
    public void ACircle_IsFourCurves_FilledOrStroked()
    {
        var document = new PdfDocument("t");
        var page = document.AddPage();
        page.FillCircle(100, 100, 50, new PdfColor(1, 0, 0));
        page.StrokeCircle(300, 100, 20, 2, PdfColor.Black);

        var text = Text(document.Build());

        // Four curves each, from the rightmost point round the top, the left, the bottom and back: control points a little over half a radius out.
        Assert.Contains("q 1 0 0 rg 150 100 m 150 127.61 127.61 150 100 150 c 72.39 150 50 127.61 50 100 c 50 72.39 72.39 50 100 50 c 127.61 50 150 72.39 150 100 c h f Q", text, StringComparison.Ordinal);
        Assert.Contains("q 0 0 0 RG 2 w 320 100 m 320 111.05 311.05 120 300 120 c 288.95 120 280 111.05 280 100 c 280 88.95 288.95 80 300 80 c 311.05 80 320 88.95 320 100 c h S Q", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void ACircle_NeedsAPositiveRadius(double radius)
    {
        var page = new PdfDocument("t").AddPage();

        Assert.Throws<ArgumentOutOfRangeException>(() => page.FillCircle(10, 10, radius, PdfColor.Black));
        Assert.Throws<ArgumentOutOfRangeException>(() => page.StrokeCircle(10, 10, radius, 1, PdfColor.Black));
    }

    [Fact]
    public void AColour_CanBeWrittenAsAWebColour()
    {
        var colour = PdfColor.FromRgb(0x1B2A49);

        Assert.Equal(27 / 255.0, colour.Red, 6);
        Assert.Equal(42 / 255.0, colour.Green, 6);
        Assert.Equal(73 / 255.0, colour.Blue, 6);
    }
}
