using ExamPlatform.Modules.QuestionBank.Application.Ports;

namespace ExamPlatform.Modules.QuestionBank.Infrastructure.Encryption;

/// <summary>The <see cref="IQuestionContentEncryptionStatus"/> that reads the content scan the backfill uses, so the status and the backfill agree.</summary>
/// <param name="backfill">The scan of the stored content values.</param>
public sealed class QuestionContentEncryptionStatus(QuestionContentBackfill backfill) : IQuestionContentEncryptionStatus
{
    /// <inheritdoc />
    public async Task<ContentEncryptionStatusDto> ReadAsync(CancellationToken cancellationToken)
    {
        var scan = await backfill.ReadStatusAsync(cancellationToken);
        return new ContentEncryptionStatusDto(scan.ContentValues, scan.PlaintextValues, scan.IsComplete);
    }
}
