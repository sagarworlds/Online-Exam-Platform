using ExamPlatform.Modules.Analytics.Application;
using ExamPlatform.Modules.Analytics.Contracts;

namespace ExamPlatform.Modules.Analytics.UnitTests;

/// <summary>The names downloaded reports are saved under.</summary>
public class ReportFileNameTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 9, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void AnExamName_BecomesLowercaseWordsJoinedByHyphens()
    {
        Assert.Equal("item-analysis-physics-final-20261010-0930.csv", ReportFileName.ForItemAnalysis("Physics Final", Now));
    }

    [Fact]
    public void PunctuationAndNonLatinLetters_SeparateWords_SoNothingCanEscapeTheFileName()
    {
        // Slashes, quotes and accented letters are not allowed to reach the file name, where they could break the header or the folder.
        Assert.Equal(
            "item-analysis-mock-2-n-code-quotes-20261010-0930.csv",
            ReportFileName.ForItemAnalysis("Mock #2 — Ünïcode & 'quotes'", Now));
        Assert.DoesNotContain("..", ReportFileName.ForItemAnalysis("../../etc", Now));
    }

    [Fact]
    public void AnExamNameWithNothingUsable_IsNamedExam()
    {
        Assert.Equal("item-analysis-exam-20261010-0930.csv", ReportFileName.ForItemAnalysis("   ", Now));
        Assert.Equal("item-analysis-exam-20261010-0930.csv", ReportFileName.ForItemAnalysis("परीक्षा", Now));
    }

    [Fact]
    public void TheTimeInTheName_IsUtc()
    {
        Assert.Equal("item-analysis-x-20260101-0005.csv", ReportFileName.ForItemAnalysis("x", new DateTime(2026, 1, 1, 0, 5, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void APdfReport_IsNamedWithAPdfExtension()
    {
        Assert.Equal("item-analysis-physics-final-20261010-0930.pdf", ReportFileName.ForItemAnalysis("Physics Final", Now, ReportFormat.Pdf));
    }

    [Fact]
    public void ACandidateAnalyticsFile_IsNamedForTheTime_AndCarriesNoIdentity()
    {
        Assert.Equal("candidate-analytics-20261010-0930.csv", ReportFileName.ForCandidateAnalytics(Now, ReportFormat.Csv));
        Assert.Equal("candidate-analytics-20261010-0930.pdf", ReportFileName.ForCandidateAnalytics(Now, ReportFormat.Pdf));
    }
}
