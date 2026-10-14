using System.Globalization;
using System.Text;
using ExamPlatform.Modules.Analytics.Application.Ports;
using ExamPlatform.Modules.Analytics.Contracts;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.Analytics.Infrastructure;

/// <summary>
/// Writes an item analysis as CSV (FR-38): one row per question, with the figures as plain numbers a spreadsheet can sum and sort.
/// </summary>
/// <remarks>
/// Column names are in English so the file reads the same wherever it is opened. A withheld figure is an empty cell with a status that says why,
/// so a blank is never mistaken for zero. The file carries a byte-order mark, which tells spreadsheet programs it is UTF-8; without it, Hindi and
/// Marathi question text opens as garbled characters.
/// </remarks>
public sealed class ItemAnalysisCsvWriter : IItemAnalysisCsvWriter
{
    // The time and drop-off columns come last, after the figures already in the file, so a reader that finds a column by its place keeps the
    // same columns. Each group ends with its status column, so a withheld figure always says why.
    private static readonly string[] Header =
    [
        "No.", "Question", "Attempts", "Correct", "Difficulty (%)", "Discrimination", "Status", "Average time (s)", "Time status",
        "Reached", "Dropped off", "Drop-off (%)", "Drop-off status",
    ];

    /// <inheritdoc />
    public byte[] Write(ExamItemAnalysisDto analysis)
    {
        var text = new StringBuilder(Csv.WriteRow(Header));
        foreach (var row in analysis.Questions)
            text.Append(Csv.WriteRow(FieldsOf(row, analysis.MinimumCohortSize)));

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text.ToString())];
    }

    private static IEnumerable<string?> FieldsOf(ItemRowDto row, int minimumCohortSize) =>
    [
        row.Position.ToString(CultureInfo.InvariantCulture),
        ReportFormatting.SafeCell(row.Text),
        row.Attempts.ToString(CultureInfo.InvariantCulture),
        row.CorrectCount.ToString(CultureInfo.InvariantCulture),
        ReportFormatting.Figure(row.Difficulty * 100),
        ReportFormatting.Discrimination(row.Discrimination),
        ReportFormatting.ItemStatus(row, minimumCohortSize),
        ReportFormatting.Figure(row.Time.AverageSeconds),
        ReportFormatting.TimeStatus(row.Time, minimumCohortSize),
        ReportFormatting.Count(row.DropOff.Reached),
        ReportFormatting.Count(row.DropOff.DroppedOff),
        ReportFormatting.Figure(row.DropOff.Rate * 100),
        ReportFormatting.DropOffStatus(row.DropOff, minimumCohortSize),
    ];
}
