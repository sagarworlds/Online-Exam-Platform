using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain;

/// <summary>The option, or the set of options, a candidate has chosen for one question of an <see cref="Attempt"/>.</summary>
public sealed class AttemptAnswer : Entity
{
    /// <summary>The attempt this answer belongs to.</summary>
    public Guid AttemptId { get; private set; }

    /// <summary>The question-bank id of the question answered.</summary>
    public Guid QuestionId { get; private set; }

    /// <summary>
    /// The options chosen: exactly one for a single-answer question, one or more for a multiple-answer one. Never empty: clearing a
    /// question removes the whole answer, so a stored answer always means "a candidate chose these".
    /// </summary>
    public Guid[] SelectedOptionIds { get; private set; } = [];

    /// <summary>The first option chosen. For a single-answer question, the option chosen.</summary>
    public Guid SelectedOptionId => SelectedOptionIds.FirstOrDefault();

    /// <summary>When the answer was last saved.</summary>
    public DateTime AnsweredAtUtc { get; private set; }

    // For EF Core.
    private AttemptAnswer() : base(Guid.Empty)
    {
    }

    internal AttemptAnswer(Guid attemptId, Guid questionId, IReadOnlyCollection<Guid> selectedOptionIds, DateTime answeredAtUtc) : base(Guid.NewGuid())
    {
        AttemptId = attemptId;
        QuestionId = questionId;
        SelectedOptionIds = selectedOptionIds.ToArray();
        AnsweredAtUtc = answeredAtUtc;
    }

    internal void Change(IReadOnlyCollection<Guid> selectedOptionIds, DateTime answeredAtUtc)
    {
        SelectedOptionIds = selectedOptionIds.ToArray();
        AnsweredAtUtc = answeredAtUtc;
    }
}
