using System.Globalization;
using ExamPlatform.Modules.Analytics.Contracts;

namespace ExamPlatform.Modules.Analytics.Infrastructure;

/// <summary>
/// The figure formats, status words and text rules that the CSV and PDF reports share, so one figure reads the same in both files (FR-38).
/// </summary>
internal static class ReportFormatting
{
    /// <summary>The line placed at the end of a PDF whose text had a character the built-in font cannot print.</summary>
    /// <remarks>
    /// A question mark in the PDF stands for a letter the file does not show. The reader is told so, and told where the full text is, rather
    /// than being left to take a row of question marks for the question.
    /// </remarks>
    public const string SubstitutionNote =
        "Some characters could not be printed with this PDF's built-in font and are shown as '?'. The CSV export of the same figures has the full text.";

    /// <summary>A whole count, written as a plain integer.</summary>
    public static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>A mark, written with up to two decimals and no trailing zeros, as a score is printed.</summary>
    public static string Score(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>A figure to two decimals, or an empty cell when there is none, so a withheld figure is never read as zero.</summary>
    /// <param name="value">The figure, or null when it is withheld.</param>
    /// <returns>The figure's text, or null for an empty cell.</returns>
    public static string? Figure(decimal? value) => value is { } v ? v.ToString("0.##", CultureInfo.InvariantCulture) : null;

    /// <summary>A discrimination index to four decimals, or an empty cell when it is withheld.</summary>
    /// <param name="value">The index, from -1 to 1, or null when it is withheld.</param>
    /// <returns>The index's text, or null for an empty cell.</returns>
    public static string? Discrimination(decimal? value) => value is { } v ? v.ToString("0.0000", CultureInfo.InvariantCulture) : null;

    /// <summary>
    /// Says why an item's figures are withheld, in words a reader can filter on, so a blank is never taken for zero.
    /// </summary>
    /// <param name="row">The question's row.</param>
    /// <param name="minimumCohortSize">How many candidates a question must have had before its indices are shown.</param>
    /// <returns>The status: why the difficulty is withheld, why the discrimination is, or that both are shown.</returns>
    public static string ItemStatus(ItemRowDto row, int minimumCohortSize)
    {
        if (row.Difficulty is null)
            return $"Not shown: fewer than {minimumCohortSize.ToString(CultureInfo.InvariantCulture)} candidates had the question";

        return row.Discrimination is null
            ? "Difficulty shown; discrimination not shown: no one from a group had the question"
            : "Shown";
    }

    /// <summary>
    /// Says why a question's average time is withheld, in words a reader can filter on, so a blank is never taken for zero (FR-38).
    /// </summary>
    /// <param name="time">The question's time figure.</param>
    /// <param name="minimumCohortSize">How many candidates must have a time for the question before its average is shown.</param>
    /// <returns>The status: that the average is shown, or why it is not.</returns>
    public static string TimeStatus(ItemTimeDto time, int minimumCohortSize) => time.Status switch
    {
        ItemTimeStatus.Shown => "Shown",
        ItemTimeStatus.BelowMinimumCohort => $"Not shown: fewer than {Count(minimumCohortSize)} candidates had a time for the question",
        ItemTimeStatus.NoTimes => "Not shown: no candidate had a time for the question",
        _ => throw new ArgumentOutOfRangeException(nameof(time), time.Status, "This time status has no report wording."),
    };

    /// <summary>
    /// Spreadsheet programs run a cell that begins with <c>=</c>, <c>+</c>, <c>-</c> or <c>@</c> as a formula, so text beginning that way could
    /// execute something when the file is opened. A leading apostrophe makes the program show the text instead. Applied to text only: the
    /// figures are numbers and their minus signs must stay.
    /// </summary>
    /// <param name="text">A text field from the report, such as a question or an exam name.</param>
    /// <returns>The text, guarded when it begins with a formula character.</returns>
    public static string SafeCell(string text) =>
        text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + text : text;

    /// <summary>A time as the reports print it: UTC, to the minute, in a form a spreadsheet reads as a date.</summary>
    /// <param name="utc">The instant, in UTC.</param>
    /// <returns>The time's text.</returns>
    public static string UtcStamp(DateTime utc) => utc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
}
