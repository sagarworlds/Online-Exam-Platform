using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ExamPlatform.SharedKernel.Application.Pdf;

namespace ExamPlatform.SharedKernel.UnitTests.Pdf;

/// <summary>The report layout: tables that run over pages with a repeated header, wrapped and cut cells, footers, and text kept off the margins (FR-38).</summary>
public class PdfReportTests
{
    private static readonly IReadOnlyList<PdfColumn> Columns =
    [
        new("Reference", 120),
        new("Note", 300),
        new("Score", 60, RightAligned: true),
    ];

    private static string Text(byte[] pdf) => Encoding.Latin1.GetString(pdf);

    private static int PageCount(string text) => Regex.Matches(text, @"/Type /Page\b").Count;

    /// <summary>Rows of three cells, numbered, so a long table is long enough to run over several pages.</summary>
    private static IEnumerable<IReadOnlyList<string>> Rows(int count) =>
        Enumerable.Range(1, count).Select(i => (IReadOnlyList<string>)new[] { $"R{i:D3}", $"Note for row {i}", i.ToString(CultureInfo.InvariantCulture) });

    [Fact]
    public void AShortReport_IsOnOnePage()
    {
        var report = new PdfReport("Short");
        report.Heading("Short report");
        report.Table(Columns, Rows(3));

        var text = Text(report.Build());

        Assert.Equal(1, PageCount(text));
        Assert.Contains("(Page 1 of 1) Tj", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ALongTable_RunsOverSeveralPages()
    {
        var report = new PdfReport("Long");
        report.Table(Columns, Rows(300));

        var text = Text(report.Build());

        Assert.True(PageCount(text) > 3, $"expected several pages, found {PageCount(text)}");
    }

    [Fact]
    public void ALongTable_RepeatsItsHeader_OnEveryPage()
    {
        var report = new PdfReport("Repeated");
        report.Table(Columns, Rows(300));

        var text = Text(report.Build());

        Assert.Equal(PageCount(text), Regex.Matches(text, @"\(Reference\) Tj").Count);
    }

    [Fact]
    public void EachPage_HasItsFooter_WithThePageNumberAndTheTitle()
    {
        var report = new PdfReport("Quarterly figures");
        report.Table(Columns, Rows(300));

        var text = Text(report.Build());
        var pages = PageCount(text);

        for (var i = 1; i <= pages; i++)
            Assert.Contains($"(Page {i} of {pages}) Tj", text, StringComparison.Ordinal);
        Assert.Equal(pages, Regex.Matches(text, @"\(Quarterly figures\) Tj").Count);
    }

    [Fact]
    public void NoText_IsDrawnBelowTheBottomMargin_ExceptInTheFooter()
    {
        // Every baseline that is not a footer must sit above the bottom margin, or a row would run into the footer band.
        var report = new PdfReport("Margins");
        report.Heading("Margins");
        report.Paragraph(string.Join(' ', Enumerable.Repeat("A paragraph long enough to wrap onto several lines of the page.", 40)));
        report.Table(Columns, Rows(200));

        var text = Text(report.Build());
        var baselines = Regex.Matches(text, @"(\d+(?:\.\d+)?) (\d+(?:\.\d+)?) Td").Select(m => double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));

        Assert.All(baselines, y => Assert.True(y == 30 || y >= PdfReport.BottomMargin - 12, $"a baseline at {y} is in the footer band or below the margin"));
    }

    [Fact]
    public void ACellTooLongForTheRow_IsCutWithThreeDots_AndKeepsItsRowHeightBounded()
    {
        var essay = string.Join(' ', Enumerable.Repeat("a long sentence that will not fit", 60));
        var report = new PdfReport("Cut");
        report.Table(Columns, [new[] { "R1", essay, "1" }]);

        var text = Text(report.Build());

        Assert.Contains("...) Tj", text, StringComparison.Ordinal);
        // The page holds three header cells, two footer texts, the row's reference and figure, and at most MaxCellLines lines of the essay.
        var drawn = Regex.Matches(text, @"\) Tj").Count;
        Assert.True(drawn <= 3 + 2 + 1 + 1 + PdfReport.MaxCellLines, $"{drawn} texts were drawn");
    }

    [Fact]
    public void ACellWithBrackets_IsEscaped_SoItPrintsAsWritten()
    {
        var report = new PdfReport("Escape");
        report.Table(Columns, [new[] { "R1", @"Asha (Kumar) \ Rao", "1" }]);

        var text = Text(report.Build());

        Assert.Contains(@"(Asha \(Kumar\) \\ Rao) Tj", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ARightAlignedFigure_SitsAgainstTheColumnsRightEdge()
    {
        var report = new PdfReport("Align");
        report.Table(Columns, [new[] { "R1", "n", "12345" }]);

        var text = Text(report.Build());
        var figureX = double.Parse(Regex.Match(text, @"(\d+(?:\.\d+)?) \d+(?:\.\d+)? Td \(12345\) Tj").Groups[1].Value, CultureInfo.InvariantCulture);

        // The column starts at 50 + 120 + 300 = 470 and ends at 530; a right-aligned figure ends inside that edge.
        Assert.True(figureX > 470 + 30, $"the figure starts at {figureX}, which is too far left for a right-aligned column");
    }

    [Fact]
    public void TextTheFontCannotPrint_IsReplaced_AndTheReportSaysSo()
    {
        var report = new PdfReport("Substitution");
        report.Table(Columns, [new[] { "R1", "अशा", "1" }]);

        var text = Text(report.Build());

        Assert.True(report.SubstitutedCharacters);
        Assert.Contains("(???) Tj", text, StringComparison.Ordinal);
    }

    [Fact]
    public void LineBreaksAndTabs_AreLayout_NotSubstitutions()
    {
        var report = new PdfReport("Breaks");
        report.Paragraph("First line\nsecond line\tand a tab");

        Assert.False(report.SubstitutedCharacters);
    }

    [Fact]
    public void ColumnsWiderThanTheMargins_AreRefused()
    {
        var report = new PdfReport("Wide");
        IReadOnlyList<PdfColumn> wide = [new("A", 300), new("B", 300)];

        Assert.Throws<ArgumentException>(() => report.Table(wide, []));
    }

    [Fact]
    public void ARowWithTheWrongNumberOfCells_IsRefused()
    {
        var report = new PdfReport("Shape");

        Assert.Throws<ArgumentException>(() => report.Table(Columns, [new[] { "only one" }]));
    }

    [Fact]
    public void ATableWithNoRows_IsAHeaderAlone()
    {
        var report = new PdfReport("Empty");
        report.Table(Columns, []);

        var text = Text(report.Build());

        Assert.Equal(1, PageCount(text));
        Assert.Contains("(Reference) Tj", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ALongParagraph_WrapsAcrossPages_WithoutLosingWords()
    {
        var words = Enumerable.Range(1, 2000).Select(i => $"w{i}").ToArray();
        var report = new PdfReport("Paragraph");
        report.Paragraph(string.Join(' ', words));

        var text = Text(report.Build());

        Assert.True(PageCount(text) > 1);
        // The last word shares its line with others, so it is checked at the end of a drawn line.
        Assert.Contains("w2000) Tj", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Building_TwiceIsRefused_SoAFooterIsNotWrittenTwice()
    {
        var report = new PdfReport("Once");
        report.Paragraph("text");
        report.Build();

        Assert.Throws<InvalidOperationException>(() => report.Build());
    }
}
