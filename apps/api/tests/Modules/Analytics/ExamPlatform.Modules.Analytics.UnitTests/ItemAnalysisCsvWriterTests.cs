using System.Text;
using ExamPlatform.Modules.Analytics.Contracts;
using ExamPlatform.Modules.Analytics.Infrastructure;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.Analytics.UnitTests;

/// <summary>The CSV file of an item analysis (FR-38): what a spreadsheet reads back from it.</summary>
public class ItemAnalysisCsvWriterTests
{
    private static ExamItemAnalysisDto Analysis(params ItemRowDto[] rows) =>
        new(Guid.NewGuid(), "Physics", ResultsReleased: true, CandidateCount: 44, MinimumCohortSize: 30, GroupSize: 12, rows);

    /// <summary>Reads the file back as rows of fields, dropping the byte-order mark the writer puts first.</summary>
    private static IReadOnlyList<IReadOnlyList<string>> ReadBack(byte[] bytes) =>
        Csv.Parse(Encoding.UTF8.GetString(bytes.AsSpan(3)));

    private static ItemRowDto Shown(int position, string text, decimal difficulty, decimal discrimination) =>
        new(Guid.NewGuid(), position, text, 44, 31, difficulty, discrimination);

    [Fact]
    public void TheFile_HasAHeader_ThenOneRowPerQuestionInTheExamsOrder()
    {
        var bytes = new ItemAnalysisCsvWriter().Write(Analysis(
            Shown(1, "First", 0.7045m, 0.4545m),
            new ItemRowDto(Guid.NewGuid(), 2, "Second", 12, 6, null, null)));

        var rows = ReadBack(bytes);

        Assert.Equal(new[] { "No.", "Question", "Attempts", "Correct", "Difficulty (%)", "Discrimination", "Status" }, rows[0]);
        Assert.Equal("1", rows[1][0]);
        Assert.Equal("2", rows[2][0]);
    }

    [Fact]
    public void AShownFigure_IsAPlainNumber_WithItsSign()
    {
        var rows = ReadBack(new ItemAnalysisCsvWriter().Write(Analysis(Shown(1, "Q", 0.7045m, -0.1m))));

        Assert.Equal("70.45", rows[1][4]);
        Assert.Equal("-0.1000", rows[1][5]);
        Assert.Equal("Shown", rows[1][6]);
    }

    [Fact]
    public void AWithheldFigure_IsAnEmptyCell_WithAStatusThatSaysWhy()
    {
        // A blank must never read as zero, so the status column says the figure is not shown, and by how many candidates it fell short.
        var rows = ReadBack(new ItemAnalysisCsvWriter().Write(Analysis(new ItemRowDto(Guid.NewGuid(), 1, "Q", 12, 6, null, null))));

        Assert.Equal(string.Empty, rows[1][4]);
        Assert.Equal(string.Empty, rows[1][5]);
        Assert.Equal("Not shown: fewer than 30 candidates had the question", rows[1][6]);
    }

    [Fact]
    public void AQuestionTextWithCommasAndQuotes_IsQuoted_SoItStaysInItsColumn()
    {
        var rows = ReadBack(new ItemAnalysisCsvWriter().Write(Analysis(Shown(1, "Define \"work\", in joules", 0.5m, 0m))));

        Assert.Equal("Define \"work\", in joules", rows[1][1]);
        Assert.Equal(7, rows[1].Count);
    }

    [Fact]
    public void AQuestionTextThatLooksLikeAFormula_IsDefused_ButAFigureKeepsItsMinusSign()
    {
        // A question beginning "=" would run as a formula when the file is opened; the apostrophe makes the spreadsheet show it as text.
        var rows = ReadBack(new ItemAnalysisCsvWriter().Write(Analysis(Shown(1, "=HYPERLINK(\"x\")", 0.5m, -0.5m))));

        Assert.Equal("'=HYPERLINK(\"x\")", rows[1][1]);
        Assert.Equal("-0.5000", rows[1][5]);
    }

    [Fact]
    public void TheFile_StartsWithAByteOrderMark_SoHindiAndMarathiOpenCorrectly()
    {
        var bytes = new ItemAnalysisCsvWriter().Write(Analysis(Shown(1, "प्रश्न", 0.5m, 0m)));

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3));
        Assert.Equal("प्रश्न", ReadBack(bytes)[1][1]);
    }
}
