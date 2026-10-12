using System.Text;
using System.Text.RegularExpressions;
using ExamPlatform.Modules.Analytics.Contracts;
using ExamPlatform.Modules.Analytics.Infrastructure;

namespace ExamPlatform.Modules.Analytics.UnitTests;

/// <summary>The PDF report of an item analysis (FR-38): the same figures and status words as the CSV, laid out over as many pages as needed.</summary>
public class ItemAnalysisPdfWriterTests
{
    // The summary of an analysis whose results are held or whose cohort has no marks: no figures, no bands.
    private static readonly ExamScoreSummaryDto NoScores = new(0, null, null, []);

    // The empty time figures: the PDF table has no time column, so these only fill the record.
    private static readonly ExamTimeSummaryDto NoTime = new(0, null);
    private static readonly ItemTimeDto NoItemTime = new(0, null, ItemTimeStatus.NoTimes);

    private static ExamItemAnalysisDto Analysis(params ItemRowDto[] rows) =>
        new(Guid.NewGuid(), "Physics", ResultsReleased: true, CandidateCount: 44, MinimumCohortSize: 30, GroupSize: 12, rows, NoScores, NoTime);

    private static ItemRowDto Shown(int position, string text) => new(Guid.NewGuid(), position, text, 44, 31, 0.7045m, 0.4545m, NoItemTime);

    private static string Text(byte[] pdf) => Encoding.Latin1.GetString(pdf);

    private static int PageCount(string text) => Regex.Matches(text, @"/Type /Page\b").Count;

    [Fact]
    public void TheFile_IsAPdf_WithAHeaderAndAnEndMarker()
    {
        var text = Text(new ItemAnalysisPdfWriter().Write(Analysis(Shown(1, "First"))));

        Assert.StartsWith("%PDF-1.4", text, StringComparison.Ordinal);
        Assert.EndsWith("%%EOF\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFigures_AreTheSameAsTheCsv_ToTheDigit()
    {
        var text = Text(new ItemAnalysisPdfWriter().Write(Analysis(Shown(1, "First"))));

        // The CSV writes difficulty as 70.45 and discrimination as 0.4545; the report prints the same words.
        Assert.Contains("(70.45) Tj", text, StringComparison.Ordinal);
        Assert.Contains("(0.4545) Tj", text, StringComparison.Ordinal);
        Assert.Contains("(Shown) Tj", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AWithheldFigure_ShowsItsStatus_NotAZero()
    {
        // The status wraps inside its column, so only its opening words, which sit on the first line, are checked as one run.
        var row = new ItemRowDto(Guid.NewGuid(), 1, "Thin", 12, 6, null, null, NoItemTime);

        var text = Text(new ItemAnalysisPdfWriter().Write(Analysis(row)));

        Assert.Contains("Not shown: fewer", text, StringComparison.Ordinal);
        Assert.DoesNotContain("(0) Tj", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ALongQuestionList_RunsOverSeveralPages_WithTheHeaderOnEach()
    {
        var rows = Enumerable.Range(1, 250).Select(i => Shown(i, $"Question number {i}")).ToArray();

        var text = Text(new ItemAnalysisPdfWriter().Write(Analysis(rows)));
        var pages = PageCount(text);

        Assert.True(pages > 3, $"expected several pages, found {pages}");
        Assert.Equal(pages, Regex.Matches(text, @"\(Discrimination\) Tj").Count);
        Assert.Contains($"(Page {pages} of {pages}) Tj", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheResultsNotReleased_SaysSo_AndDrawsNoTable()
    {
        var analysis = new ExamItemAnalysisDto(Guid.NewGuid(), "Physics", ResultsReleased: false, CandidateCount: 0, MinimumCohortSize: 30, GroupSize: 12, [], NoScores, NoTime);

        var text = Text(new ItemAnalysisPdfWriter().Write(analysis));

        Assert.Contains("The results have not been released", text, StringComparison.Ordinal);
        Assert.DoesNotContain("(Discrimination) Tj", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnExamWithNoQuestions_SaysSo()
    {
        var text = Text(new ItemAnalysisPdfWriter().Write(Analysis()));

        Assert.Contains("This exam has no questions to show.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AQuestionInAScriptTheFontLacks_ShowsAMark_AndTheReportSaysWhy()
    {
        var text = Text(new ItemAnalysisPdfWriter().Write(Analysis(Shown(1, "अशा"))));

        Assert.Contains("(???) Tj", text, StringComparison.Ordinal);
        // The note wraps across two lines, so its opening words are the part that sits on one line.
        Assert.Contains("Some characters could not be printed", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AReportWithNoSubstitution_DoesNotSayOne()
    {
        var text = Text(new ItemAnalysisPdfWriter().Write(Analysis(Shown(1, "Plain English"))));

        Assert.DoesNotContain("CSV export of the same figures", text, StringComparison.Ordinal);
    }
}
