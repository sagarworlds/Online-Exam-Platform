using ExamPlatform.Modules.QuestionBank.Application.Dtos;
using ExamPlatform.Modules.QuestionBank.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Domain;
using ExamPlatform.Modules.QuestionBank.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.QuestionBank.Application.Commands;

/// <summary>Imports questions from a CSV, Excel or JSON file (FR-6).</summary>
/// <param name="Content">The file's contents: text for CSV and JSON, base64 for Excel. A table's first row is the header and is not imported as a question.</param>
/// <param name="CreatedBy">The authoring user, taken from the caller's token.</param>
/// <param name="Format">How <paramref name="Content"/> is written; CSV unless said otherwise.</param>
/// <param name="AllowDuplicates">Create a row even when the bank, or an earlier row of the file, already has the same question (FR-9).</param>
public sealed record ImportQuestionsCommand(string Content, Guid CreatedBy, QuestionFileFormat Format = QuestionFileFormat.Csv, bool AllowDuplicates = false);

/// <summary>
/// Handles <see cref="ImportQuestionsCommand"/>. Every row is checked under the exact rules
/// <see cref="CreateQuestionHandler"/> enforces for one question (<see cref="QuestionText.Clean"/>,
/// <see cref="Question.Create"/>), so a question created by import can never be one the single-question form would
/// have refused. A bad row does not stop the ones around it: each is validated on its own, and the caller gets a
/// report of what was created and what was not, rather than an all-or-nothing failure that cannot say which row
/// was the problem.
/// </summary>
public sealed class ImportQuestionsHandler(
    IQuestionRepository repository, QuestionDuplicateFinder duplicates, QuestionDuplicatePolicy duplicatePolicy, IQuestionBankUnitOfWork unitOfWork, IRichTextSanitizer sanitizer, Clock clock)
{
    /// <summary>The most data rows one import processes; a larger file is refused outright rather than run partway.</summary>
    public const int MaxRows = 1000;

    /// <summary>Validates and creates every row it can.</summary>
    /// <param name="command">The file to import.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="BulkImportTooLargeError">The file has more than <see cref="MaxRows"/> data rows.</exception>
    /// <exception cref="BulkImportUnreadableError">The file cannot be read as the format it was sent in.</exception>
    public async Task<ImportQuestionsResultDto> HandleAsync(ImportQuestionsCommand command, CancellationToken cancellationToken)
    {
        var dataRows = QuestionFiles.Read(command.Format, command.Content);
        if (dataRows.Count > MaxRows)
            throw new BulkImportTooLargeError(MaxRows);

        var created = new List<ImportedQuestionDto>();
        var rejected = new List<RejectedRowDto>();
        var skipped = new List<DuplicateRowDto>();
        var seenInFile = new Dictionary<string, int>();

        foreach (var (line, fields, error) in dataRows)
        {
            try
            {
                if (fields is null)
                    throw new InvalidQuestionError(error ?? "The row could not be read.");

                var (text, options, allowsMultiple, difficulty, topics, isTextAnswer, acceptedAnswers) = QuestionCsvRow.Parse(fields);
                var cleaned = QuestionText.Clean(sanitizer, text);

                // A text question is a repeat of another when its accepted answers are, so those stand in for its options here.
                var answersForRepeats = isTextAnswer ? acceptedAnswers.Select(answer => new NewQuestionOption(answer, true)).ToList() : options;

                // A repeat is left out rather than rejected: nothing is wrong with the row, the bank just has it. Rows that repeat an
                // earlier row of the same file are caught here, since that row is not stored until the end.
                if (duplicatePolicy.Refuse && !command.AllowDuplicates && (RepeatOfEarlierRow(cleaned.PlainText, answersForRepeats, seenInFile, line) ?? await FindStoredAsync(cleaned.PlainText, answersForRepeats, cancellationToken)) is { } reason)
                {
                    skipped.Add(new DuplicateRowDto(line, reason));
                    continue;
                }

                var question = Question.Create(
                    cleaned.Html, options, command.CreatedBy, clock.UtcNow,
                    chapterId: null, QuestionDifficultyText.Parse(difficulty), topics, allowsMultiple,
                    isTextAnswer: isTextAnswer, acceptedAnswers: acceptedAnswers);
                question.IndexText(cleaned.PlainText);

                repository.Add(question);
                created.Add(new ImportedQuestionDto(line, question.Id));
            }
            catch (InvalidQuestionError ex)
            {
                rejected.Add(new RejectedRowDto(line, [ex.Message]));
            }
        }

        // One save for every row that validated, so a row rejected after another was added does not undo it:
        // the report and what is actually stored can never disagree about which rows made it in.
        if (created.Count > 0)
            await unitOfWork.SaveChangesAsync(cancellationToken);

        return new ImportQuestionsResultDto(created, rejected, skipped);
    }

    private async Task<string?> FindStoredAsync(string plainText, IReadOnlyList<NewQuestionOption> options, CancellationToken cancellationToken)
    {
        var same = (await duplicates.FindAsync(plainText, options.Select(o => o?.Text ?? string.Empty).ToList(), null, cancellationToken)).FirstOrDefault(m => m.SameOptions);
        return same is null ? null : $"The bank already has this question ({same.Question.Id}).";
    }

    private static string? RepeatOfEarlierRow(string plainText, IReadOnlyList<NewQuestionOption> options, Dictionary<string, int> seenInFile, int line)
    {
        var key = QuestionFingerprint.KeyOf(plainText);
        if (key.Length == 0)
            return null;

        key += "|" + QuestionFingerprint.OptionsKeyOf(options.Select(o => o?.Text ?? string.Empty));
        if (seenInFile.TryGetValue(key, out var firstLine))
            return $"Same as row {firstLine} of this file.";

        seenInFile[key] = line;
        return null;
    }
}
