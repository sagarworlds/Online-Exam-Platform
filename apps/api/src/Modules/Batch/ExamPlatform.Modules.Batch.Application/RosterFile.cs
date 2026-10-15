using ExamPlatform.Modules.Batch.Domain;
using ExamPlatform.Modules.Batch.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;
using BatchAggregate = ExamPlatform.Modules.Batch.Domain.Batch;

namespace ExamPlatform.Modules.Batch.Application;

/// <summary>A roster file read into rows, with what the reader did not use.</summary>
/// <param name="Rows">The rows that have anything in them, in file order. A blank line is not a row.</param>
/// <param name="IgnoredColumns">The columns the file has that a roster does not use, so the report can say they were left out.</param>
public sealed record RosterFileContents(IReadOnlyList<RosterRow> Rows, IReadOnlyList<string> IgnoredColumns);

/// <summary>
/// Reads a CSV roster (FR-50): an e-mail address and, optionally, a phone number for each candidate. It only reads;
/// whether an address or a number is usable is the batch's call (<see cref="BatchAggregate.ImportMembers"/>), so a file and a
/// single add can never disagree.
/// </summary>
/// <remarks>
/// The header row is optional because a roster is often a column of addresses copied out of another sheet. A first row
/// with a recognised column name (e-mail or phone) is the header and its columns are found by name, in any order.
/// Otherwise the first column is the e-mail address, the second the phone number, and every row is data. A header
/// that is not recognised is therefore read as data and reported as an invalid address, not guessed at.
/// </remarks>
public static class RosterFile
{
    private const char ByteOrderMark = '\uFEFF';
    private const int MaxReportedNameLength = 40;

    private static readonly HashSet<string> EmailNames = ["email", "emailaddress", "emailid", "mail"];
    private static readonly HashSet<string> PhoneNames = ["phone", "phonenumber", "mobile", "mobilenumber"];

    /// <summary>Reads a roster file from its text.</summary>
    /// <param name="content">The CSV text.</param>
    /// <returns>The data rows, numbered as in the file (a header counts as row 1), and the columns that were not used.</returns>
    /// <exception cref="RosterUnreadableError">The file is empty, has only a header, or has a header with no e-mail column.</exception>
    public static RosterFileContents Read(string? content)
    {
        // Excel's "CSV UTF-8" starts the file with a byte-order mark; left in, it would sit in front of the first
        // address and make a good one look invalid.
        var records = Csv.Parse((content ?? string.Empty).TrimStart(ByteOrderMark));

        // Blank lines before the first row are skipped, so a header is still found after them.
        var firstFilled = IndexOfFirstFilledRecord(records);
        if (firstFilled < 0)
            throw new RosterUnreadableError("The file is empty. Send a CSV with one candidate's e-mail address per row.");

        var (emailColumn, phoneColumn, firstDataIndex, ignored) = ReadLayout(records, firstFilled);

        var rows = new List<RosterRow>();
        for (var i = firstDataIndex; i < records.Count; i++)
        {
            var fields = records[i];
            if (IsBlank(fields))
                continue;

            // The row number is the record's place in the file, blank lines included, so it matches what the person
            // sees when they open the file again to fix a row.
            rows.Add(new RosterRow(i + 1, FieldAt(fields, emailColumn), FieldAt(fields, phoneColumn)));
        }

        if (rows.Count == 0)
            throw new RosterUnreadableError("The file has a header but no rows. Add one candidate's e-mail address per row.");

        return new RosterFileContents(rows, ignored);
    }

    private static (int Email, int Phone, int FirstDataIndex, IReadOnlyList<string> Ignored) ReadLayout(
        IReadOnlyList<IReadOnlyList<string>> records, int firstFilled)
    {
        var first = records[firstFilled];
        var emailColumn = IndexOfColumn(first, EmailNames);
        var phoneColumn = IndexOfColumn(first, PhoneNames);

        if (emailColumn < 0 && phoneColumn < 0)
        {
            // No header: the first row is a candidate. Columns past the second are not used.
            var widest = records.Max(r => r.Count);
            var ignoredPositions = Enumerable.Range(3, Math.Max(0, widest - 2)).Select(n => $"Column {n}").ToList();
            return (0, 1, firstFilled, ignoredPositions);
        }

        if (emailColumn < 0)
            throw new RosterUnreadableError("The header row has no e-mail column. Name the column \"email\".");

        var ignored = first
            .Select((name, index) => (name, index))
            .Where(c => c.index != emailColumn && c.index != phoneColumn && !string.IsNullOrWhiteSpace(c.name))
            .Select(c => ShortenForReport(c.name.Trim()))
            .ToList();
        return (emailColumn, phoneColumn, firstFilled + 1, ignored);
    }

    private static int IndexOfFirstFilledRecord(IReadOnlyList<IReadOnlyList<string>> records)
    {
        for (var i = 0; i < records.Count; i++)
        {
            if (!IsBlank(records[i]))
                return i;
        }

        return -1;
    }

    private static bool IsBlank(IReadOnlyList<string> fields) => fields.All(string.IsNullOrWhiteSpace);

    private static int IndexOfColumn(IReadOnlyList<string> header, HashSet<string> names)
    {
        for (var i = 0; i < header.Count; i++)
        {
            // "E-mail address", "e_mail" and "EMAIL" are one name: spaces, dashes and underscores carry no meaning.
            var key = new string(header[i].Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
            if (names.Contains(key))
                return i;
        }

        return -1;
    }

    private static string? FieldAt(IReadOnlyList<string> fields, int column) =>
        column >= 0 && column < fields.Count && !string.IsNullOrWhiteSpace(fields[column]) ? fields[column] : null;

    // A header is the uploader's own text and goes back into the report; keeping it short stops a file with a huge
    // header from bloating the response.
    private static string ShortenForReport(string name) =>
        name.Length <= MaxReportedNameLength ? name : name[..MaxReportedNameLength] + "…";
}
