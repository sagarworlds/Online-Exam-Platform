using ExamPlatform.SharedKernel.Application.Pdf;

namespace ExamPlatform.SharedKernel.UnitTests.Pdf;

/// <summary>The text rules of the PDF writer: what the fonts print, how text is escaped, and how a line is measured and wrapped.</summary>
public class PdfTextTests
{
    [Theory]
    [InlineData("Asha Kumar", true)]
    [InlineData("Café Müller", true)]
    [InlineData("Asha (Kumar) \\ 100%", true)]
    [InlineData("अशा कुमार", true)]
    [InlineData("क्षत्रिय", true)]
    [InlineData("क\u200Dष", true)]
    [InlineData("\u200Dक", false)]
    [InlineData("Ā-long vowel", false)]
    [InlineData("Ellipsis…", false)]
    [InlineData("漢字", false)]
    [InlineData("tab\there", false)]
    [InlineData("line\nbreak", false)]
    public void CanShow_AcceptsOnlyWhatTheFontsHave(string text, bool expected) =>
        Assert.Equal(expected, PdfText.CanShow(text));

    [Theory]
    [InlineData("Café Müller", true)]
    [InlineData("अशा कुमार", false)]
    public void CanShowLatin_AcceptsOnlyWhatTheHelveticaFacesHave(string text, bool expected) =>
        Assert.Equal(expected, PdfText.CanShowLatin(text));

    [Fact]
    public void Printable_ReplacesEachCharacterNeitherLatinNorDevanagari_WithAQuestionMark()
    {
        // Two Chinese characters are neither set, so each becomes one mark; the Devanagari beside them is kept as it is.
        Assert.Equal("Asha ?? Kumar", PdfText.Printable("Asha 漢字 Kumar"));
        Assert.Equal("Asha अशा Kumar", PdfText.Printable("Asha अशा Kumar"));
    }

    [Fact]
    public void Printable_KeepsAJoinerOnlyAfterADevanagariLetter()
    {
        Assert.Equal("क\u200Dष", PdfText.Printable("क\u200Dष"));
        Assert.Equal("a?b", PdfText.Printable("a\u200Db"));
    }

    [Fact]
    public void Printable_TurnsLineBreaksAndTabsIntoSpaces_AndCollapsesRuns()
    {
        Assert.Equal("one two three", PdfText.Printable("one\r\n  two\tthree  "));
    }

    [Fact]
    public void Printable_ReplacesASurrogatePairOnce()
    {
        // One emoji is two UTF-16 units; it is one character to a reader, so one mark stands for it.
        Assert.Equal("a ? b", PdfText.Printable("a \U0001F600 b"));
    }

    [Fact]
    public void Printable_ResultAlwaysPassesCanShow()
    {
        var printable = PdfText.Printable("Mixed अ text 😀 with \t tabs and ßtraße");

        Assert.True(PdfText.CanShow(printable));
    }

    [Fact]
    public void Escape_BackslashesAndBrackets_AreEscapedSoTheTextPrintsAsWritten()
    {
        Assert.Equal(@"Asha \(Kumar\) \\ Rao", PdfText.Escape(@"Asha (Kumar) \ Rao"));
    }

    [Fact]
    public void Width_UsesTheStandardHelveticaMetrics()
    {
        // Helvetica sets 'a' at 556 and 'i' at 222 thousandths of an em, so at 10pt "ai" is 7.78 points.
        Assert.Equal(5.56, PdfText.Width("a", PdfFont.Regular, 10), precision: 6);
        Assert.Equal(7.78, PdfText.Width("ai", PdfFont.Regular, 10), precision: 6);
        Assert.Equal(9.44, PdfText.Width("W", PdfFont.Bold, 10), precision: 6);
        Assert.Equal(2.78, PdfText.Width(" ", PdfFont.Regular, 10), precision: 6);
    }

    [Fact]
    public void Width_OfEveryPrintableAsciiCharacter_IsPositive()
    {
        // A misaligned width table would leave some character at zero or shift every later one, so every printable character is checked.
        for (var c = ' '; c <= '~'; c++)
        {
            Assert.True(PdfText.Width(c.ToString(), PdfFont.Regular, 100) > 0, $"regular width of '{c}'");
            Assert.True(PdfText.Width(c.ToString(), PdfFont.Bold, 100) > 0, $"bold width of '{c}'");
        }
    }

    [Fact]
    public void Wrap_BreaksAtSpaces_IntoLinesNoWiderThanTheLimit()
    {
        var text = string.Join(' ', Enumerable.Repeat("Bartholomew", 6));

        var lines = PdfText.Wrap(text, PdfFont.Bold, 24, 450);

        Assert.True(lines.Count > 1);
        Assert.All(lines, line => Assert.True(PdfText.Width(line, PdfFont.Bold, 24) <= 450));
        Assert.Equal(text, string.Join(' ', lines));
    }

    [Fact]
    public void Wrap_OfShortText_IsOneLine()
    {
        Assert.Equal(["Asha Kumar"], PdfText.Wrap("Asha Kumar", PdfFont.Regular, 10, 400));
    }

    [Fact]
    public void Wrap_OfEmptyText_HasNoLines()
    {
        Assert.Empty(PdfText.Wrap("   ", PdfFont.Regular, 10, 400));
    }

    [Fact]
    public void Wrap_BreaksAWordWiderThanALine_WithoutInsertingSpacesInsideIt()
    {
        var url = new string('x', 80);

        var lines = PdfText.Wrap(url, PdfFont.Regular, 10, 100);

        Assert.True(lines.Count > 1);
        Assert.Equal(url, string.Concat(lines));
        Assert.All(lines, line => Assert.True(PdfText.Width(line, PdfFont.Regular, 10) <= 100));
    }

    [Fact]
    public void Wrap_ContinuesAfterABrokenWord_OnTheLineAfterItsLastPiece()
    {
        // At 10pt and 60 points, twelve 'x' fill a line exactly; the last six stay open, so " end" follows them on the same line.
        var lines = PdfText.Wrap(new string('x', 30) + " end", PdfFont.Regular, 10, 60);

        Assert.Equal(["xxxxxxxxxxxx", "xxxxxxxxxxxx", "xxxxxx end"], lines);
    }

    [Fact]
    public void Wrap_WithANonPositiveWidth_IsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PdfText.Wrap("text", PdfFont.Regular, 10, 0));
    }

    [Fact]
    public void Truncate_LeavesAFittingLineAlone()
    {
        Assert.Equal("short", PdfText.Truncate("short", PdfFont.Regular, 10, 200));
    }

    [Fact]
    public void Ellipsize_AlwaysEndsInThreeDots_EvenWhenTheLineAlreadyFits()
    {
        Assert.Equal("short...", PdfText.Ellipsize("short", PdfFont.Regular, 10, 200));
    }

    [Fact]
    public void Truncate_CutsALongLine_AndMarksTheCutWithThreeDots()
    {
        var line = string.Join(' ', Enumerable.Repeat("word", 40));

        var cut = PdfText.Truncate(line, PdfFont.Regular, 8, 120);

        Assert.EndsWith("...", cut, StringComparison.Ordinal);
        Assert.True(PdfText.Width(cut, PdfFont.Regular, 8) <= 120);
        Assert.StartsWith("word word", cut, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(PdfFont.TimesRoman, 2222)]
    [InlineData(PdfFont.TimesBold, 2278)]
    [InlineData(PdfFont.TimesItalic, 2222)]
    public void Width_UsesTheStandardTimesMetrics(PdfFont font, int unitsOfHello)
    {
        // H, e, l, l, o, from the Times AFM files: a width is in thousandths of an em.
        Assert.Equal(unitsOfHello * 10 / 1000.0, PdfText.Width("Hello", font, 10), 6);
    }

    [Theory]
    [InlineData(PdfFont.TimesRoman)]
    [InlineData(PdfFont.TimesBold)]
    [InlineData(PdfFont.TimesItalic)]
    public void Width_OfEveryPrintableAsciiCharacter_IsPositive_InTheTimesFaces(PdfFont font)
    {
        for (var c = ' '; c <= '~'; c++)
            Assert.True(PdfText.Width(c.ToString(), font, 10) > 0, $"'{c}' has a width");
    }

    [Fact]
    public void Width_WithLetterSpacing_AddsOneGapLessThanTheCharacters()
    {
        var plain = PdfText.Width("ABC", PdfFont.TimesBold, 10);

        Assert.Equal(plain + 2 * 3.5, PdfText.Width("ABC", PdfFont.TimesBold, 10, 3.5), 6);
        Assert.Equal(plain, PdfText.Width("ABC", PdfFont.TimesBold, 10, 0), 6);
        Assert.Equal(PdfText.Width("A", PdfFont.TimesBold, 10), PdfText.Width("A", PdfFont.TimesBold, 10, 9), 6);
    }
}
