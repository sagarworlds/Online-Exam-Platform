using ExamPlatform.Modules.QuestionBank.Application.Dtos;
using ExamPlatform.Modules.QuestionBank.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Domain;
using ExamPlatform.Modules.QuestionBank.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.QuestionBank.Application.Commands;

/// <summary>Imports questions from a CSV file (FR-6).</summary>
/// <param name="Csv">The file's contents; its first row is the header and is not imported as a question.</param>
/// <param name="CreatedBy">The authoring user, taken from the caller's token.</param>
public sealed record ImportQuestionsCommand(string Csv, Guid CreatedBy);

/// <summary>
/// Handles <see cref="ImportQuestionsCommand"/>. Every row is checked under the exact rules
/// <see cref="CreateQuestionHandler"/> enforces for one question (<see cref="QuestionText.Clean"/>,
/// <see cref="Question.Create"/>), so a question created by import can never be one the single-question form would
/// have refused. A bad row does not stop the ones around it: each is validated on its own, and the caller gets a
/// report of what was created and what was not, rather than an all-or-nothing failure that cannot say which row
/// was the problem.
/// </summary>
public sealed class ImportQuestionsHandler(
    IQuestionRepository repository, IQuestionBankUnitOfWork unitOfWork, IRichTextSanitizer sanitizer, Clock clock)
{
    /// <summary>The most data rows one import processes; a larger file is refused outright rather than run partway.</summary>
    public const int MaxRows = 1000;

    /// <summary>Validates and creates every row it can.</summary>
    /// <param name="command">The file to import.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="BulkImportTooLargeError">The file has more than <see cref="MaxRows"/> data rows.</exception>
    public async Task<ImportQuestionsResultDto> HandleAsync(ImportQuestionsCommand command, CancellationToken cancellationToken)
    {
        var rows = Csv.Parse(command.Csv);
        var dataRows = rows.Skip(1).ToList(); // Row 1 is the header; it names no question.
        if (dataRows.Count > MaxRows)
            throw new BulkImportTooLargeError(MaxRows);

        var created = new List<ImportedQuestionDto>();
        var rejected = new List<RejectedRowDto>();

        for (var i = 0; i < dataRows.Count; i++)
        {
            var line = i + 2; // +1 for the header, +1 because lines are counted from 1, not 0.
            try
            {
                var (text, options, allowsMultiple, difficulty, topics) = QuestionCsvRow.Parse(dataRows[i]);
                var cleaned = QuestionText.Clean(sanitizer, text);

                var question = Question.Create(
                    cleaned.Html, options, command.CreatedBy, clock.UtcNow,
                    chapterId: null, QuestionDifficultyText.Parse(difficulty), topics, allowsMultiple);
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

        return new ImportQuestionsResultDto(created, rejected);
    }
}
