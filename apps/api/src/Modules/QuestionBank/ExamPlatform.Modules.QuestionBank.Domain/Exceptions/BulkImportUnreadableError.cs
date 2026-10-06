using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.QuestionBank.Domain.Exceptions;

/// <summary>An import file could not be read as the format it was sent in, so there are no rows to check (FR-6).</summary>
/// <param name="reason">What is wrong with the file, safe to show to whoever sent it.</param>
public sealed class BulkImportUnreadableError(string reason) : DomainException(reason)
{
    /// <inheritdoc />
    public override string ErrorCode => "bulk_import_unreadable";

    /// <inheritdoc />
    public override int HttpStatusCode => 400;
}
