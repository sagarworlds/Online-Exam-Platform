using System.Text;
using System.Text.RegularExpressions;
using ExamPlatform.Modules.Analytics.Contracts;
using ExamPlatform.Modules.Analytics.Infrastructure;

namespace ExamPlatform.Modules.Analytics.UnitTests;

/// <summary>The candidate's analytics as a PDF report (FR-38): the trend and the section figures, over as many pages as the history needs.</summary>
public class CandidateAnalyticsPdfWriterTests
{
    private static readonly DateTime Sep1 = new(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);

    private static ScorePointDto Point(string exam, decimal score, decimal max, decimal? percent, DateTime? at = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), exam, at ?? Sep1, score, max, percent);

    private static SectionPerformanceDto Section(string name, int correct, int wrong, decimal? accuracy) =>
        new(name, 2, correct, wrong, 0, 1, accuracy);

    private static readonly CandidateAnalyticsDto Sample = new(
        2,
        [Point("Physics Mock", 37.75m, 80m, 47.19m), Point("Chemistry Test", 60m, 80m, 75m, Sep1.AddDays(7))],
        [Section("Mechanics", 3, 5, 37.5m), Section("Organic", 6, 2, 75m)], [], new TimePerQuestionDto(0, null, []), []);

    private static string Text(byte[] pdf) => Encoding.Latin1.GetString(pdf);

    [Fact]
    public void TheFile_IsAPdfWithAnEndMarker()
    {
        var text = Text(new CandidateAnalyticsPdfWriter().Write(Sample));

        Assert.StartsWith("%PDF-1.4", text, StringComparison.Ordinal);
        Assert.EndsWith("%%EOF\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheReport_HasTheTrendAndTheSections_WithTheExamNamesAndFigures()
    {
        var text = Text(new CandidateAnalyticsPdfWriter().Write(Sample));

        Assert.Contains("(Score trend) Tj", text, StringComparison.Ordinal);
        Assert.Contains("(Results by section, weakest accuracy first) Tj", text, StringComparison.Ordinal);
        Assert.Contains("(Physics Mock) Tj", text, StringComparison.Ordinal);
        Assert.Contains("(47.19) Tj", text, StringComparison.Ordinal);
        Assert.Contains("(Mechanics) Tj", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ACandidateWithNoResults_SaysSo_AndDrawsNoTables()
    {
        var text = Text(new CandidateAnalyticsPdfWriter().Write(new CandidateAnalyticsDto(0, [], [], [], new TimePerQuestionDto(0, null, []), [])));

        Assert.Contains("No results have been released to you yet", text, StringComparison.Ordinal);
        Assert.DoesNotContain("(Score trend) Tj", text, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(text, @"/Type /Page\b"));
    }

    [Fact]
    public void ALongHistory_RunsOverSeveralPages_WithTheTrendHeaderOnEach()
    {
        var trend = Enumerable.Range(1, 150).Select(i => Point($"Mock {i}", i, 100, i, Sep1.AddDays(i))).ToArray();
        var figures = new CandidateAnalyticsDto(trend.Length, trend, [Section("All", 1, 1, 50)], [], new TimePerQuestionDto(0, null, []), []);

        var text = Text(new CandidateAnalyticsPdfWriter().Write(figures));
        var pages = Regex.Matches(text, @"/Type /Page\b").Count;

        Assert.True(pages > 2, $"expected several pages, found {pages}");
        Assert.Equal(pages, Regex.Matches(text, @"\(Exam\) Tj").Count);
    }

    [Fact]
    public void AnExamName_InAScriptTheFontLacks_ShowsAMark_AndTheReportSaysWhy()
    {
        var figures = new CandidateAnalyticsDto(1, [Point("अशा परीक्षा", 1, 2, 50)], [], [], new TimePerQuestionDto(0, null, []), []);

        var text = Text(new CandidateAnalyticsPdfWriter().Write(figures));

        Assert.Contains("(??? ???????) Tj", text, StringComparison.Ordinal);
        Assert.Contains("Some characters could not be printed", text, StringComparison.Ordinal);
    }
}
