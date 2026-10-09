using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain;

/// <summary>
/// A candidate's note to themselves that they want to come back to a question of an <see cref="Attempt"/> (FR-18, "mark for review").
/// </summary>
/// <remarks>
/// Kept apart from <see cref="AttemptAnswer"/> on purpose. A mark is independent of an answer (an unanswered question can be marked,
/// and an answered one too) and it never affects the score, so nothing that reads answers (the scorer, the review, the check that
/// stops a question's answer key changing once it has been answered) has to know marks exist. A question is marked while its row
/// exists; unmarking deletes the row.
/// </remarks>
public sealed class AttemptMark : Entity
{
    /// <summary>The attempt this mark belongs to.</summary>
    public Guid AttemptId { get; private set; }

    /// <summary>The question-bank id of the question marked.</summary>
    public Guid QuestionId { get; private set; }

    /// <summary>When the question was marked.</summary>
    public DateTime MarkedAtUtc { get; private set; }

    // For EF Core.
    private AttemptMark() : base(Guid.Empty)
    {
    }

    internal AttemptMark(Guid attemptId, Guid questionId, DateTime markedAtUtc) : base(Guid.NewGuid())
    {
        AttemptId = attemptId;
        QuestionId = questionId;
        MarkedAtUtc = markedAtUtc;
    }
}
