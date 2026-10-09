using ExamPlatform.Modules.QuestionBank.Domain;
using ExamPlatform.Modules.QuestionBank.Domain.Exceptions;

namespace ExamPlatform.Modules.QuestionBank.Application;

/// <summary>
/// The one CSV shape import and export agree on (FR-6), so a file this bank exported is always a file it can
/// re-import unchanged: one row per question, up to <see cref="Question.MaxOptions"/> options as fixed column
/// pairs (an unused pair is left blank, so a 2-option question's later columns are simply empty), text as the
/// raw sanitized HTML the question is stored as, topics joined with <c>;</c> (commas are the column delimiter).
/// Chapter placement is not part of the file: it stays the separate bulk action it already is
/// (<c>POST /v1/questions/placement</c>), so this shape does not have to resolve a chapter by name across books.
/// </summary>
internal static class QuestionCsvRow
{
    /// <summary>The header row, in column order; also the number of columns every data row must have.</summary>
    public static readonly string[] Header =
    [
        "Text",
        "Option1", "Correct1", "Option2", "Correct2", "Option3", "Correct3",
        "Option4", "Correct4", "Option5", "Correct5", "Option6", "Correct6",
        "AllowsMultiple", "Difficulty", "Topics",
    ];

    /// <summary>Writes one question as a data row, in <see cref="Header"/>'s column order.</summary>
    public static string[] From(Question question)
    {
        var fields = new string[Header.Length];
        fields[0] = question.Text;
        var byOrder = question.Options.OrderBy(o => o.Order).ToList();
        for (var slot = 0; slot < Question.MaxOptions; slot++)
        {
            var option = slot < byOrder.Count ? byOrder[slot] : null;
            fields[1 + slot * 2] = option?.Text ?? string.Empty;
            fields[2 + slot * 2] = option is null ? string.Empty : option.IsCorrect ? "true" : "false";
        }

        fields[13] = question.AllowsMultiple ? "true" : "false";
        fields[14] = QuestionDifficultyText.Format(question.Difficulty) ?? string.Empty;
        fields[15] = string.Join(';', question.Topics);
        return fields;
    }

    /// <summary>Reads one data row into what <see cref="Domain.Question.Create"/> needs.</summary>
    /// <param name="row">The row's fields, in <see cref="Header"/>'s column order.</param>
    /// <exception cref="InvalidQuestionError">The row does not have exactly as many columns as <see cref="Header"/>.</exception>
    public static (string? Text, List<NewQuestionOption> Options, bool AllowsMultiple, string? Difficulty, List<string?> Topics) Parse(
        IReadOnlyList<string> row)
    {
        if (row.Count != Header.Length)
            throw new InvalidQuestionError($"Expected {Header.Length} columns, found {row.Count}.");

        var options = new List<NewQuestionOption>();
        for (var slot = 0; slot < Question.MaxOptions; slot++)
        {
            var text = row[1 + slot * 2];
            if (string.IsNullOrWhiteSpace(text))
                continue; // An empty slot means this question has fewer than the maximum options.

            options.Add(new NewQuestionOption(text, IsTruthy(row[2 + slot * 2])));
        }

        var topics = row[15].Split(';', StringSplitOptions.TrimEntries).Where(t => t.Length > 0).Cast<string?>().ToList();
        return (row[0], options, IsTruthy(row[13]), row[14], topics);
    }

    private static bool IsTruthy(string value) =>
        value.Trim().ToLowerInvariant() is "true" or "1" or "yes" or "y";
}
