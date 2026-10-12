using System.Globalization;
using System.Text;
using ExamPlatform.Modules.Analytics.Application.Ports;
using ExamPlatform.Modules.Analytics.Contracts;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.Analytics.Infrastructure;

/// <summary>
/// Writes a candidate's analytics as CSV (FR-38): the score trend as one block of rows, a blank line, then the section figures as a second block,
/// each block with its own header row.
/// </summary>
/// <remarks>
/// Two blocks rather than one table because the two kinds of figure do not share columns; a single table would leave most cells empty. Figures
/// that are withheld (no marks available, nothing answered) are empty cells, so a blank is never read as zero. The file carries a byte-order
/// mark so an exam name in Hindi or Marathi opens correctly.
/// </remarks>
public sealed class CandidateAnalyticsCsvWriter : ICandidateAnalyticsCsvWriter
{
    private static readonly string[] TrendHeader = ["Exam", "Submitted (UTC)", "Score", "Out of", "Percent of marks"];
    private static readonly string[] SectionHeader = ["Section", "Results", "Correct", "Wrong", "Partial", "Unanswered", "Accuracy (%)"];

    /// <inheritdoc />
    public byte[] Write(CandidateAnalyticsDto analytics)
    {
        var text = new StringBuilder(Csv.WriteRow(TrendHeader));
        foreach (var point in analytics.Trend)
        {
            text.Append(Csv.WriteRow([
                ReportFormatting.SafeCell(point.ExamName),
                ReportFormatting.UtcStamp(point.SubmittedAtUtc),
                ReportFormatting.Score(point.Score),
                ReportFormatting.Score(point.MaxScore),
                ReportFormatting.Figure(point.PercentOfMarks),
            ]));
        }

        // A blank line separates the blocks; a spreadsheet and the parser both read it as an empty row between them.
        text.Append("\r\n");
        text.Append(Csv.WriteRow(SectionHeader));
        foreach (var section in analytics.Sections)
        {
            text.Append(Csv.WriteRow([
                ReportFormatting.SafeCell(section.Name),
                ReportFormatting.Count(section.ResultCount),
                ReportFormatting.Count(section.CorrectCount),
                ReportFormatting.Count(section.WrongCount),
                ReportFormatting.Count(section.PartialCount),
                ReportFormatting.Count(section.UnansweredCount),
                ReportFormatting.Figure(section.Accuracy),
            ]));
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text.ToString())];
    }
}
