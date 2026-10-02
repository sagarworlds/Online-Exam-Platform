using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain;

/// <summary>The option a candidate has chosen for one question of an <see cref="Attempt"/>.</summary>
public sealed class AttemptAnswer : Entity
{
    /// <summary>The attempt this answer belongs to.</summary>
    public Guid AttemptId { get; private set; }

    /// <summary>The question-bank id of the question answered.</summary>
    public Guid QuestionId { get; private set; }

    /// <summary>The option chosen.</summary>
    public Guid SelectedOptionId { get; private set; }

    /// <summary>When the answer was last saved.</summary>
    public DateTime AnsweredAtUtc { get; private set; }

    // For EF Core.
    private AttemptAnswer() : base(Guid.Empty)
    {
    }

    internal AttemptAnswer(Guid attemptId, Guid questionId, Guid selectedOptionId, DateTime answeredAtUtc) : base(Guid.NewGuid())
    {
        AttemptId = attemptId;
        QuestionId = questionId;
        SelectedOptionId = selectedOptionId;
        AnsweredAtUtc = answeredAtUtc;
    }

    internal void Change(Guid selectedOptionId, DateTime answeredAtUtc)
    {
        SelectedOptionId = selectedOptionId;
        AnsweredAtUtc = answeredAtUtc;
    }
}
