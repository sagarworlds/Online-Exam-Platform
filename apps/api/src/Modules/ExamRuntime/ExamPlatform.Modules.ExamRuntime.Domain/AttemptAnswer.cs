using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain;

/// <summary>
/// What a candidate has answered for one question of an <see cref="Attempt"/>: the option, or the set of options, they chose, or, for a
/// text question, the answer they typed. Exactly one of the two is ever stored; clearing a question removes the whole answer, so a stored
/// answer always means "a candidate answered this".
/// </summary>
public sealed class AttemptAnswer : Entity
{
    /// <summary>The attempt this answer belongs to.</summary>
    public Guid AttemptId { get; private set; }

    /// <summary>The question-bank id of the question answered.</summary>
    public Guid QuestionId { get; private set; }

    /// <summary>
    /// The options chosen: exactly one for a single-answer question, one or more for a multiple-answer one. Empty for a text question,
    /// whose answer is <see cref="AnswerText"/> instead.
    /// </summary>
    public Guid[] SelectedOptionIds { get; private set; } = [];

    /// <summary>
    /// The answer a candidate typed to a text question, as they typed it; null for a question answered by choosing options. It is marked
    /// by <c>TypedAnswer.Matches</c>, which ignores case and extra spaces, so the stored text is kept exactly as typed.
    /// </summary>
    public string? AnswerText { get; private set; }

    /// <summary>The first option chosen. For a single-answer question, the option chosen.</summary>
    public Guid SelectedOptionId => SelectedOptionIds.FirstOrDefault();

    /// <summary>When the answer was last saved.</summary>
    public DateTime AnsweredAtUtc { get; private set; }

    // For EF Core.
    private AttemptAnswer() : base(Guid.Empty)
    {
    }

    internal AttemptAnswer(
        Guid attemptId, Guid questionId, IReadOnlyCollection<Guid> selectedOptionIds, DateTime answeredAtUtc, string? answerText = null) : base(Guid.NewGuid())
    {
        AttemptId = attemptId;
        QuestionId = questionId;
        SelectedOptionIds = selectedOptionIds.ToArray();
        AnswerText = answerText;
        AnsweredAtUtc = answeredAtUtc;
    }

    // Replaces the answer with the other kind when a candidate switches: a typed answer is dropped by choosing options, and the
    // reverse, so the stored answer is only ever one kind.
    internal void Change(IReadOnlyCollection<Guid> selectedOptionIds, DateTime answeredAtUtc, string? answerText = null)
    {
        SelectedOptionIds = selectedOptionIds.ToArray();
        AnswerText = answerText;
        AnsweredAtUtc = answeredAtUtc;
    }
}
