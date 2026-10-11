using ExamPlatform.Modules.QuestionBank.Application.Ports;

namespace ExamPlatform.Modules.QuestionBank.Application.Queries;

/// <summary>Answers the admin status: whether the bank's question content is all encrypted (#57).</summary>
/// <param name="status">Reads the stored values.</param>
public sealed class ReadContentEncryptionStatusHandler(IQuestionContentEncryptionStatus status)
{
    /// <summary>Returns the encryption state of the content, as it is now.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<ContentEncryptionStatusDto> HandleAsync(CancellationToken cancellationToken) => status.ReadAsync(cancellationToken);
}
