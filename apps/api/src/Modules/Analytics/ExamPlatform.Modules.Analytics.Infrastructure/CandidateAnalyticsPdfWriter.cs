using System.Globalization;
using ExamPlatform.Modules.Analytics.Application.Ports;
using ExamPlatform.Modules.Analytics.Contracts;
using ExamPlatform.SharedKernel.Application.Pdf;

namespace ExamPlatform.Modules.Analytics.Infrastructure;

/// <summary>
/// Writes a candidate's analytics as a PDF report (FR-38): a summary of what the figures mean, the score trend in the order the exams were sat,
/// then the section figures with the weakest first, as the analytics page orders them.
/// </summary>
/// <remarks>
/// A candidate with no released result gets a one-line report that says so, not an empty table. The report prints the same figures as the CSV;
/// a substituted character ends the report with the same note the item analysis carries.
/// </remarks>
public sealed class CandidateAnalyticsPdfWriter : ICandidateAnalyticsPdfWriter
{
    private static readonly IReadOnlyList<PdfColumn> TrendColumns =
    [
        new("Exam", 195),
        new("Submitted (UTC)", 105),
        new("Score", 65, RightAligned: true),
        new("Out of", 65, RightAligned: true),
        new("Percent of marks", 65, RightAligned: true),
    ];

    private static readonly IReadOnlyList<PdfColumn> SectionColumns =
    [
        new("Section", 147),
        new("Results", 52, RightAligned: true),
        new("Correct", 52, RightAligned: true),
        new("Wrong", 52, RightAligned: true),
        new("Partial", 52, RightAligned: true),
        new("Unanswered", 60, RightAligned: true),
        new("Accuracy (%)", 80, RightAligned: true),
    ];

    /// <inheritdoc />
    public byte[] Write(CandidateAnalyticsDto analytics)
    {
        var report = new PdfReport("Candidate analytics");
        report.Heading("Candidate analytics");

        if (analytics.ResultCount == 0)
        {
            report.Paragraph("No results have been released to you yet, so there are no figures to report.");
            return report.Build();
        }

        var counted = analytics.ResultCount.ToString(CultureInfo.InvariantCulture);
        report.Paragraph(
            $"Released results: {counted}. Times are in UTC. Percent of marks is the score over the marks available. " +
            "Accuracy is the share of answered questions that were fully correct. A blank figure had nothing to be worked from.");

        report.SubHeading("Score trend");
        report.Table(TrendColumns, analytics.Trend.Select(point => (IReadOnlyList<string>)new[]
        {
            point.ExamName,
            ReportFormatting.UtcStamp(point.SubmittedAtUtc),
            ReportFormatting.Score(point.Score),
            ReportFormatting.Score(point.MaxScore),
            ReportFormatting.Figure(point.PercentOfMarks) ?? string.Empty,
        }));

        report.SubHeading("Results by section, weakest accuracy first");
        if (analytics.Sections.Count == 0)
        {
            report.Paragraph("No section figures are available.");
        }
        else
        {
            report.Table(SectionColumns, analytics.Sections.Select(section => (IReadOnlyList<string>)new[]
            {
                section.Name,
                ReportFormatting.Count(section.ResultCount),
                ReportFormatting.Count(section.CorrectCount),
                ReportFormatting.Count(section.WrongCount),
                ReportFormatting.Count(section.PartialCount),
                ReportFormatting.Count(section.UnansweredCount),
                ReportFormatting.Figure(section.Accuracy) ?? string.Empty,
            }));
        }

        if (report.SubstitutedCharacters)
            report.Paragraph(ReportFormatting.SubstitutionNote);

        return report.Build();
    }
}
