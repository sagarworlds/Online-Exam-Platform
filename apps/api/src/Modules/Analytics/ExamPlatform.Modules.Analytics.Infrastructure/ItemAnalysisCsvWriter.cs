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
    private static readonly string[] Header = ["No.", "Question", "Attempts", "Correct", "Difficulty (%)", "Discrimination", "Status"];

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
        SafeText(row.Text),
        row.Attempts.ToString(CultureInfo.InvariantCulture),
        row.CorrectCount.ToString(CultureInfo.InvariantCulture),
        row.Difficulty is { } difficulty ? (difficulty * 100).ToString("0.##", CultureInfo.InvariantCulture) : null,
        row.Discrimination is { } discrimination ? discrimination.ToString("0.0000", CultureInfo.InvariantCulture) : null,
        StatusOf(row, minimumCohortSize),
    ];

    /// <summary>
    /// Says why a figure is empty, in words a spreadsheet reader can filter on.
    /// </summary>
    private static string StatusOf(ItemRowDto row, int minimumCohortSize)
    {
        if (row.Difficulty is null)
            return $"Not shown: fewer than {minimumCohortSize} candidates had the question";

        return row.Discrimination is null ? "Difficulty shown; discrimination not shown: no one from a group had the question" : "Shown";
    }

    /// <summary>
    /// Spreadsheet programs run a cell that begins with <c>=</c>, <c>+</c>, <c>-</c> or <c>@</c> as a formula, so a question whose text begins
    /// that way could execute something when the file is opened. A leading apostrophe makes the program show the text instead. Applied to text
    /// only: the figures are numbers and their minus signs must stay.
    /// </summary>
    private static string SafeText(string text) =>
        text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + text : text;
}
