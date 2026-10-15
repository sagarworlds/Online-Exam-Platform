using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain;

/// <summary>
/// The highest sequence number the server has applied to one question's answer in an <see cref="Attempt"/> (NFR-4, ADR 0004): the number the
/// client gave a save or a clear, or the next one up for a write from a client that gave none. A write is applied only when its number is higher
/// than this one, so an old request that arrives late cannot replace a newer answer, and cannot bring back one that was cleared.
/// </summary>
/// <remarks>
/// Kept apart from <see cref="AttemptAnswer"/> on purpose. Clearing a question removes its answer, but the number it reached must stay, or a
/// late save from before the clear would bring the answer back. A question nothing was ever written to has no row, as with
/// <see cref="AttemptQuestionTime"/>. The store keeps (attempt, question) unique and guards the row with a version, so two writes to the
/// same question at the same moment cannot both be applied: the loser is a conflict the client sends again.
/// </remarks>
public sealed class AttemptAnswerSequence : Entity
{
    /// <summary>The largest number a client may send: the largest whole number a browser holds exactly, 2^53 - 1.</summary>
    public const long MaxValue = 9_007_199_254_740_991;

    /// <summary>The attempt this number belongs to.</summary>
    public Guid AttemptId { get; private set; }

    /// <summary>The question-bank id of the question whose answer it counts writes to.</summary>
    public Guid QuestionId { get; private set; }

    /// <summary>The highest sequence number applied so far, from 1.</summary>
    public long Value { get; private set; }

    // For EF Core.
    private AttemptAnswerSequence() : base(Guid.Empty)
    {
    }

    internal AttemptAnswerSequence(Guid attemptId, Guid questionId, long value) : base(Guid.NewGuid())
    {
        AttemptId = attemptId;
        QuestionId = questionId;
        Value = value;
    }

    // Only ever called with a higher number; Attempt.ClaimAnswerSequence checks that before it raises the figure.
    internal void RaiseTo(long value) => Value = value;
}
