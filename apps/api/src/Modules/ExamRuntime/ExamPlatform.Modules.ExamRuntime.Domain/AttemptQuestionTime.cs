using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain;

/// <summary>
/// How long a candidate has spent on one question of an <see cref="Attempt"/>, in whole seconds, as the page measured it (FR-36, time per
/// question).
/// </summary>
/// <remarks>
/// Kept apart from <see cref="AttemptAnswer"/> on purpose. A candidate can read a question for minutes and leave it unanswered, and that time
/// belongs in their analytics, but a row here must never make the question look answered. The figure is a running total for the attempt, which
/// is why a repeated or late report cannot count the same seconds twice; see <see cref="Attempt.RecordQuestionTime"/>. A question the page never
/// reported on has no row.
/// </remarks>
public sealed class AttemptQuestionTime : Entity
{
    /// <summary>The attempt this time belongs to.</summary>
    public Guid AttemptId { get; private set; }

    /// <summary>The question-bank id of the question the time was spent on.</summary>
    public Guid QuestionId { get; private set; }

    /// <summary>The whole seconds spent on the question so far in this attempt.</summary>
    public int Seconds { get; private set; }

    // For EF Core.
    private AttemptQuestionTime() : base(Guid.Empty)
    {
    }

    internal AttemptQuestionTime(Guid attemptId, Guid questionId, int seconds) : base(Guid.NewGuid())
    {
        AttemptId = attemptId;
        QuestionId = questionId;
        Seconds = seconds;
    }

    // Only ever called with a higher total; Attempt.RecordQuestionTime checks that before it raises the figure.
    internal void RaiseTo(int seconds) => Seconds = seconds;
}
