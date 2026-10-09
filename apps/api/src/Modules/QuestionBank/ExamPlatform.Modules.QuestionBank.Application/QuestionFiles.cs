using System.Text;
using System.Text.Json;
using ExamPlatform.Modules.QuestionBank.Domain;
using ExamPlatform.Modules.QuestionBank.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.QuestionBank.Application;

/// <summary>The file formats questions can be imported from and exported to (FR-6).</summary>
public enum QuestionFileFormat
{
    /// <summary>Comma-separated text, one row per question (see <see cref="QuestionCsvRow"/>).</summary>
    Csv,

    /// <summary>An Excel workbook with the same columns as <see cref="Csv"/> on its first sheet.</summary>
    Xlsx,

    /// <summary>A JSON array with one object per question, options as a nested list.</summary>
    Json,
}

/// <summary>One row of an import file, ready to be validated like a question typed into the form.</summary>
/// <param name="Line">Where the row is in the file, counting from 1 (the header of a table is line 1), so a report can point at it.</param>
/// <param name="Fields">The row in <see cref="QuestionCsvRow.Header"/>'s column order; null when the row could not be read at all.</param>
/// <param name="Error">Why the row could not be read, when <paramref name="Fields"/> is null.</param>
public sealed record QuestionFileRow(int Line, IReadOnlyList<string>? Fields, string? Error = null);

/// <summary>A file an export produced.</summary>
/// <param name="Bytes">The file's contents.</param>
/// <param name="ContentType">Its media type.</param>
/// <param name="FileName">The name to offer when it is downloaded.</param>
/// <param name="Skipped">How many matching questions did not fit the format and are not in the file (see <see cref="QuestionFiles.Write"/>).</param>
public sealed record ExportedQuestionFile(byte[] Bytes, string ContentType, string FileName, int Skipped);

/// <summary>
/// Reads and writes question files in each <see cref="QuestionFileFormat"/> (FR-6). Every format is turned into the
/// same rows of text as the CSV, so a question is validated by one code path whichever file it came from, and a file
/// one format exports is one the same format imports unchanged.
/// </summary>
public static class QuestionFiles
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>Parses a format name from a request, ignoring case; null or empty means CSV, as the first version of the API was.</summary>
    /// <exception cref="InvalidQuestionError">The name is not a known format.</exception>
    public static QuestionFileFormat ParseFormat(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        null or "" or "csv" => QuestionFileFormat.Csv,
        "xlsx" or "excel" => QuestionFileFormat.Xlsx,
        "json" => QuestionFileFormat.Json,
        _ => throw new InvalidQuestionError($"Unknown file format '{name}'. Use csv, xlsx or json."),
    };

    /// <summary>
    /// Reads an import file into rows. The header row of a table format is dropped; the rows after it are returned with their
    /// line numbers.
    /// </summary>
    /// <param name="format">The file's format.</param>
    /// <param name="content">The file as text for CSV and JSON; for Excel, the file's bytes in base64.</param>
    /// <exception cref="BulkImportUnreadableError">The file cannot be read as that format at all.</exception>
    public static IReadOnlyList<QuestionFileRow> Read(QuestionFileFormat format, string content) => format switch
    {
        QuestionFileFormat.Csv => Csv.Parse(content).Skip(1).Select((fields, i) => new QuestionFileRow(i + 2, fields)).ToList(),
        QuestionFileFormat.Xlsx => ReadWorkbook(content),
        QuestionFileFormat.Json => ReadJson(content),
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    /// <summary>Writes questions in a format, in the order given.</summary>
    /// <param name="format">The format to write.</param>
    /// <param name="questions">The questions to include.</param>
    /// <remarks>
    /// Excel keeps at most <see cref="Xlsx.MaxCellLength"/> characters in a cell, which a question with embedded pictures
    /// can exceed; such questions are left out of a workbook and counted in <see cref="ExportedQuestionFile.Skipped"/>
    /// rather than written as a file Excel would refuse to open. CSV and JSON have no such limit.
    /// </remarks>
    public static ExportedQuestionFile Write(QuestionFileFormat format, IReadOnlyList<Question> questions)
    {
        switch (format)
        {
            case QuestionFileFormat.Json:
                var objects = questions.Select(ToJson).ToList();
                return new(JsonSerializer.SerializeToUtf8Bytes(objects, Json), "application/json", "questions.json", 0);
            case QuestionFileFormat.Xlsx:
                var fits = questions.Where(q => QuestionCsvRow.From(q).All(f => f.Length <= Xlsx.MaxCellLength)).ToList();
                var rows = new List<IReadOnlyList<string>> { QuestionCsvRow.Header };
                rows.AddRange(fits.Select(QuestionCsvRow.From));
                return new(Xlsx.Write(rows), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "questions.xlsx", questions.Count - fits.Count);
            default:
                var csv = new StringBuilder(Csv.WriteRow(QuestionCsvRow.Header));
                foreach (var question in questions)
                    csv.Append(Csv.WriteRow(QuestionCsvRow.From(question)));
                return new(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", "questions.csv", 0);
        }
    }

    private static IReadOnlyList<QuestionFileRow> ReadWorkbook(string base64)
    {
        try
        {
            var bytes = Convert.FromBase64String(base64);
            return Xlsx.Read(bytes).Skip(1).Select(r => new QuestionFileRow(r.Line, r.Fields)).ToList();
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException)
        {
            throw new BulkImportUnreadableError("The Excel file could not be read. Send an .xlsx workbook with the exported columns on its first sheet.");
        }
    }

    private static IReadOnlyList<QuestionFileRow> ReadJson(string text)
    {
        JsonElement root;
        try
        {
            root = JsonDocument.Parse(text).RootElement;
        }
        catch (JsonException)
        {
            throw new BulkImportUnreadableError("The file is not valid JSON.");
        }

        if (root.ValueKind != JsonValueKind.Array)
            throw new BulkImportUnreadableError("The JSON must be an array with one object per question.");

        var rows = new List<QuestionFileRow>();
        var line = 0;
        foreach (var element in root.EnumerateArray())
        {
            line++;
            rows.Add(FromJson(line, element));
        }

        return rows;
    }

    private static QuestionFileRow FromJson(int line, JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return new(line, null, "Each question must be a JSON object.");

        var fields = new string[QuestionCsvRow.Header.Length];
        Array.Fill(fields, string.Empty);
        fields[0] = Text(element, "text");

        if (element.TryGetProperty("options", out var options) && options.ValueKind == JsonValueKind.Array)
        {
            var slot = 0;
            foreach (var option in options.EnumerateArray())
            {
                if (slot == Question.MaxOptions)
                    return new(line, null, $"A question can have at most {Question.MaxOptions} options.");

                fields[1 + slot * 2] = option.ValueKind == JsonValueKind.Object ? Text(option, "text") : string.Empty;
                fields[2 + slot * 2] = option.ValueKind == JsonValueKind.Object && IsTrue(option, "isCorrect") ? "true" : "false";
                slot++;
            }
        }

        fields[13] = IsTrue(element, "allowsMultiple") ? "true" : "false";
        fields[14] = Text(element, "difficulty");
        if (element.TryGetProperty("topics", out var topics) && topics.ValueKind == JsonValueKind.Array)
            fields[15] = string.Join(';', topics.EnumerateArray().Where(t => t.ValueKind == JsonValueKind.String).Select(t => t.GetString()));

        fields[16] = IsTrue(element, "isTextAnswer") ? "true" : "false";
        if (element.TryGetProperty("acceptedAnswers", out var accepted) && accepted.ValueKind == JsonValueKind.Array)
            fields[17] = string.Join('|', accepted.EnumerateArray().Where(a => a.ValueKind == JsonValueKind.String).Select(a => a.GetString()));

        return new(line, fields);
    }

    private static object ToJson(Question question) => new
    {
        text = question.Text,
        options = question.Options.OrderBy(o => o.Order).Select(o => new { text = o.Text, isCorrect = o.IsCorrect }).ToList(),
        allowsMultiple = question.AllowsMultiple,
        difficulty = QuestionDifficultyText.Format(question.Difficulty),
        topics = question.Topics,
        isTextAnswer = question.IsTextAnswer,
        acceptedAnswers = question.AcceptedAnswers,
    };

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    private static bool IsTrue(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
