namespace ExamPlatform.Modules.QuestionBank.Application.Ports;

/// <summary>What the admin status shows about question content encryption (#57). The adapter reads the stored values, not the entities.</summary>
public interface IQuestionContentEncryptionStatus
{
    /// <summary>Counts the stored content values and how many of them are still plaintext. Changes nothing.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ContentEncryptionStatusDto> ReadAsync(CancellationToken cancellationToken);
}

/// <summary>The encryption state of the bank's content.</summary>
/// <param name="ContentValues">How many stored content values there are, across the questions, options and version history.</param>
/// <param name="PlaintextValues">How many are still plaintext; zero once the backfill has run.</param>
/// <param name="Complete">Whether every stored content value is encrypted.</param>
public sealed record ContentEncryptionStatusDto(int ContentValues, int PlaintextValues, bool Complete);
