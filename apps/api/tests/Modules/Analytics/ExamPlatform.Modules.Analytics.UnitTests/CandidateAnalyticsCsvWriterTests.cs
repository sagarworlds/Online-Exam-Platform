using System.Text;
using ExamPlatform.Modules.Analytics.Contracts;
using ExamPlatform.Modules.Analytics.Infrastructure;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.Analytics.UnitTests;

/// <summary>The candidate's analytics as CSV (FR-38): two blocks a spreadsheet reads back, with withheld figures blank and formulas guarded.</summary>
public class CandidateAnalyticsCsvWriterTests
{
    private static readonly DateTime Sep1 = new(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);

    private static ScorePointDto Point(string exam, decimal score, decimal max, decimal? percent, DateTime? at = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), exam, at ?? Sep1, score, max, percent);

    private static SectionPerformanceDto Section(string name, int correct, int wrong, decimal? accuracy) =>
        new(name, 2, correct, wrong, 0, 1, accuracy);

    private static readonly CandidateAnalyticsDto Sample = new(
        2,
        [Point("Physics Mock", 37.75m, 80m, 47.19m), Point("Chemistry Test", 60m, 80m, 75m, Sep1.AddDays(7))],
        [Section("Mechanics", 3, 5, 37.5m), Section("Organic", 6, 2, 75m)]);

    /// <summary>Reads the file back as rows, dropping the byte-order mark the writer puts first.</summary>
    private static IReadOnlyList<IReadOnlyList<string>> ReadBack(byte[] bytes) =>
        Csv.Parse(Encoding.UTF8.GetString(bytes.AsSpan(3)));

    [Fact]
    public void TheFile_HasAByteOrderMark_SoASpreadsheetReadsItAsUtf8()
    {
        var bytes = new CandidateAnalyticsCsvWriter().Write(Sample);

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3));
    }

    [Fact]
    public void TheScoreTrend_IsOneRowPerResult_UnderItsHeader()
    {
        var rows = ReadBack(new CandidateAnalyticsCsvWriter().Write(Sample));

        Assert.Equal(["Exam", "Submitted (UTC)", "Score", "Out of", "Percent of marks"], rows[0]);
        Assert.Equal(["Physics Mock", "2026-09-01 10:00", "37.75", "80", "47.19"], rows[1]);
        Assert.Equal(["Chemistry Test", "2026-09-08 10:00", "60", "80", "75"], rows[2]);
    }

    [Fact]
    public void TheSections_FollowABlankLine_UnderTheirOwnHeader()
    {
        var rows = ReadBack(new CandidateAnalyticsCsvWriter().Write(Sample));

        Assert.Equal([""], rows[3]);
        Assert.Equal(["Section", "Results", "Correct", "Wrong", "Partial", "Unanswered", "Accuracy (%)"], rows[4]);
        Assert.Equal(["Mechanics", "2", "3", "5", "0", "1", "37.5"], rows[5]);
        Assert.Equal(["Organic", "2", "6", "2", "0", "1", "75"], rows[6]);
    }

    [Fact]
    public void AWithheldFigure_IsAnEmptyCell_NotZero()
    {
        var figures = new CandidateAnalyticsDto(1, [Point("No marks", 0, 0, null)], [Section("Untried", 0, 0, null)]);

        var rows = ReadBack(new CandidateAnalyticsCsvWriter().Write(figures));

        Assert.Equal(string.Empty, rows[1][4]);
        Assert.Equal(string.Empty, rows[^1][6]);
    }

    [Fact]
    public void AnExamName_ThatStartsLikeAFormula_IsGuarded()
    {
        var figures = new CandidateAnalyticsDto(1, [Point("=HYPERLINK(\"http://x\")", 1, 2, 50)], []);

        var rows = ReadBack(new CandidateAnalyticsCsvWriter().Write(figures));

        Assert.Equal("'=HYPERLINK(\"http://x\")", rows[1][0]);
    }

    [Fact]
    public void ACandidateWithNoResults_HasTheTwoHeadersAndNoFigures()
    {
        var rows = ReadBack(new CandidateAnalyticsCsvWriter().Write(new CandidateAnalyticsDto(0, [], [])));

        Assert.Equal(3, rows.Count);
        Assert.Equal("Exam", rows[0][0]);
        Assert.Equal("Section", rows[2][0]);
    }
}
