using System.Globalization;
using ExamPlatform.Modules.Analytics.Application.Ports;
using ExamPlatform.Modules.Analytics.Contracts;
using ExamPlatform.SharedKernel.Application.Pdf;

namespace ExamPlatform.Modules.Analytics.Infrastructure;

/// <summary>
/// Writes an item analysis as a PDF report (FR-38): a summary of how the figures were worked, then one table row per question, over as many
/// pages as the exam needs, with the same difficulty and discrimination figures and status words as the CSV.
/// </summary>
/// <remarks>
/// The table is in the exam's order, and its header repeats on each page so every page reads alone. A question's text is the same short preview
/// the CSV carries, so the two files list the same questions. The average time per question is in the CSV only: the table already fills the
/// width between the margins, so another column would mean re-laying out every existing one. A question whose text uses a character the
/// built-in font lacks shows a question mark, and the report ends with a line that says so.
/// </remarks>
public sealed class ItemAnalysisPdfWriter : IItemAnalysisPdfWriter
{
    private static readonly IReadOnlyList<PdfColumn> Columns =
    [
        new("No.", 26, RightAligned: true),
        new("Question", 160),
        new("Attempts", 50, RightAligned: true),
        new("Correct", 46, RightAligned: true),
        new("Difficulty (%)", 56, RightAligned: true),
        new("Discrimination", 70, RightAligned: true),
        new("Status", 87),
    ];

    /// <inheritdoc />
    public byte[] Write(ExamItemAnalysisDto analysis)
    {
        var report = new PdfReport($"Item analysis: {analysis.ExamName}");
        report.Heading($"Item analysis: {analysis.ExamName}");

        if (!analysis.ResultsReleased)
        {
            report.Paragraph("The results have not been released, so no candidate has been counted and no question has figures yet.");
        }
        else
        {
            report.Paragraph(Summary(analysis));
            if (analysis.Questions.Count == 0)
                report.Paragraph("This exam has no questions to show.");
            else
                report.Table(Columns, RowsOf(analysis));
        }

        if (report.SubstitutedCharacters)
            report.Paragraph(ReportFormatting.SubstitutionNote);

        return report.Build();
    }

    /// <summary>Says how the figures were worked, so a reader knows what a withheld figure or an index means.</summary>
    private static string Summary(ExamItemAnalysisDto analysis)
    {
        var counted = analysis.CandidateCount.ToString(CultureInfo.InvariantCulture);
        var minimum = analysis.MinimumCohortSize.ToString(CultureInfo.InvariantCulture);
        var group = analysis.GroupSize.ToString(CultureInfo.InvariantCulture);
        return $"Candidates counted: {counted}, each by their best released result. A question's figures are shown only once at least {minimum} " +
            $"candidates had it. The discrimination index compares the top and bottom groups of {group} candidates.";
    }

    private static IEnumerable<IReadOnlyList<string>> RowsOf(ExamItemAnalysisDto analysis) =>
        analysis.Questions.Select(row => (IReadOnlyList<string>)new[]
        {
            row.Position.ToString(CultureInfo.InvariantCulture),
            row.Text,
            row.Attempts.ToString(CultureInfo.InvariantCulture),
            row.CorrectCount.ToString(CultureInfo.InvariantCulture),
            ReportFormatting.Figure(row.Difficulty * 100) ?? string.Empty,
            ReportFormatting.Discrimination(row.Discrimination) ?? string.Empty,
            ReportFormatting.ItemStatus(row, analysis.MinimumCohortSize),
        });
}
