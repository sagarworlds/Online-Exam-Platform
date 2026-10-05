using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.QuestionBank.Domain.Exceptions;

/// <summary>A CSV import named more data rows than the bank will process in one call (FR-6).</summary>
/// <param name="maxRows">The most rows one import accepts.</param>
public sealed class BulkImportTooLargeError(int maxRows) : DomainException($"A single import can have at most {maxRows} questions. Split the file and import it in parts.")
{
    /// <inheritdoc />
    public override string ErrorCode => "bulk_import_too_large";

    /// <inheritdoc />
    public override int HttpStatusCode => 400;
}
