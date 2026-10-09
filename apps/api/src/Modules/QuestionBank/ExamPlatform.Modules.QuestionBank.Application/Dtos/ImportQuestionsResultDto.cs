namespace ExamPlatform.Modules.QuestionBank.Application.Dtos;

/// <summary>One question a CSV import created.</summary>
/// <param name="Row">The CSV line it came from (counting the header as line 1), so it can be found in the file again.</param>
/// <param name="Id">The new question's id.</param>
public sealed record ImportedQuestionDto(int Row, Guid Id);

/// <summary>One row a CSV import could not use.</summary>
/// <param name="Row">The CSV line it came from (counting the header as line 1).</param>
/// <param name="Errors">Why the row was rejected, safe to show to whoever imported the file.</param>
public sealed record RejectedRowDto(int Row, IReadOnlyList<string> Errors);

/// <summary>One row an import left out because the same question already exists (FR-9).</summary>
/// <param name="Row">The line it came from (counting the header as line 1).</param>
/// <param name="Reason">What it repeats: a question in the bank, or an earlier row of the file.</param>
public sealed record DuplicateRowDto(int Row, string Reason);

/// <summary>The validation report an import returns (FR-6): what was created, what was rejected and why, and what was left out as a repeat.</summary>
/// <param name="Created">The questions the import created, in file order.</param>
/// <param name="Rejected">The rows the import left out because they are not valid questions, in file order.</param>
/// <param name="Duplicates">The rows left out because the same question already exists, in file order.</param>
public sealed record ImportQuestionsResultDto(
    IReadOnlyList<ImportedQuestionDto> Created, IReadOnlyList<RejectedRowDto> Rejected, IReadOnlyList<DuplicateRowDto> Duplicates);
