using System.Text;

namespace ExamPlatform.SharedKernel.Application;

/// <summary>
/// A minimal RFC 4180 CSV reader and writer, for the bulk import/export a module offers over HTTP (FR-6):
/// no module needs a full spreadsheet library just to read and write rows of text fields. Handles quoted
/// fields, embedded commas, embedded quotes (doubled, per the RFC) and embedded newlines inside a quoted
/// field; it does not handle anything beyond plain delimited text (no formulas, no type coercion — every
/// field is a string, and the caller parses what each column means).
/// </summary>
public static class Csv
{
    /// <summary>
    /// Parses CSV text into rows of fields. The first line is not treated specially; a caller that uses a
    /// header row reads and drops <c>rows[0]</c> itself.
    /// </summary>
    /// <param name="text">The CSV text. A trailing newline is optional; a blank line produces an empty row.</param>
    /// <returns>One list of field values per row, in order.</returns>
    public static IReadOnlyList<IReadOnlyList<string>> Parse(string text)
    {
        var rows = new List<IReadOnlyList<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var i = 0;

        void EndField()
        {
            row.Add(field.ToString());
            field.Clear();
        }

        void EndRow()
        {
            EndField();
            rows.Add(row);
            row = [];
        }

        while (i < text.Length)
        {
            var c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    // A doubled quote inside a quoted field is one literal quote; a lone one closes the field.
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i += 2;
                        continue;
                    }

                    inQuotes = false;
                    i++;
                    continue;
                }

                field.Append(c);
                i++;
                continue;
            }

            switch (c)
            {
                case '"' when field.Length == 0:
                    // Only a quote at the very start of a field opens quoting; one midway through is a literal
                    // character, the same leniency most spreadsheet programs show a hand-edited CSV.
                    inQuotes = true;
                    i++;
                    break;
                case ',':
                    EndField();
                    i++;
                    break;
                case '\r' when i + 1 < text.Length && text[i + 1] == '\n':
                    EndRow();
                    i += 2;
                    break;
                case '\r':
                case '\n':
                    EndRow();
                    i++;
                    break;
                default:
                    field.Append(c);
                    i++;
                    break;
            }
        }

        // A file ending without a trailing newline still has a last row to close; one that ends with a
        // newline has already closed its last row and would otherwise produce a spurious empty one.
        if (field.Length > 0 || row.Count > 0)
            EndRow();

        return rows;
    }

    /// <summary>Writes one row as a CSV line, quoting a field only when its content needs it, and terminates it with <c>\r\n</c>.</summary>
    /// <param name="fields">The field values, in column order.</param>
    public static string WriteRow(IEnumerable<string?> fields) =>
        string.Join(',', fields.Select(QuoteIfNeeded)) + "\r\n";

    private static string QuoteIfNeeded(string? field)
    {
        field ??= string.Empty;
        if (field.IndexOfAny([',', '"', '\r', '\n']) < 0)
            return field;

        return $"\"{field.Replace("\"", "\"\"")}\"";
    }
}
