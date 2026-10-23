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

    /// <summary>The longest reason the evaluator may give that is kept; a longer one is cut.</summary>
    public const int MaxEvaluationReasonLength = 500;

    /// <summary>The longest failure description kept.</summary>
    public const int MaxEvaluationErrorLength = 200;

    /// <summary>
    /// What the AI evaluator decided about the typed answer, or null while it has decided nothing: it was never asked, or is still being
    /// asked. An answer that matches an accepted answer word for word is right without being asked, so it never gets one. A verdict stays
    /// until a new one replaces it, so a result being checked again keeps showing the verdict it had.
    /// </summary>
    public TypedAnswerVerdict? TextVerdict { get; private set; }

    /// <summary>The evaluator's reason for its verdict, a sentence or two, for staff who are asked about the result. Never shown to the candidate.</summary>
    public string? EvaluationReason { get; private set; }

    /// <summary>The model that gave the verdict, as the service named it, so a result can be traced to what produced it.</summary>
    public string? EvaluationModel { get; private set; }

    /// <summary>When the verdict was given.</summary>
    public DateTime? EvaluatedAtUtc { get; private set; }

    /// <summary>When the answer was queued for evaluation, or null when it is not waiting for one.</summary>
    public DateTime? EvaluationRequestedAtUtc { get; private set; }

    /// <summary>Not before this instant: after a failure the next try waits, longer each time.</summary>
    public DateTime? EvaluationNextAttemptAtUtc { get; private set; }

    /// <summary>
    /// Until this instant a worker holds the answer, so that two running hosts do not ask about the same answer at once. A worker that dies
    /// holding it lets go by the clock, when the instant passes.
    /// </summary>
    public DateTime? EvaluationLeaseUntilUtc { get; private set; }

    /// <summary>How many times asking failed since the answer was queued, for backing off and for staff to see that something is wrong.</summary>
    public int EvaluationFailures { get; private set; }

    /// <summary>What went wrong the last time, in a few words and never with the answer's content in it; null when nothing has.</summary>
    public string? EvaluationLastError { get; private set; }

    /// <summary>
    /// When the platform stopped waiting for the evaluator and marked the answer by the accepted answers alone, or null when it never did
    /// (it got a verdict, is still waiting, or was never queued). <see cref="EvaluationReason"/> then says so. A check asked for by staff later
    /// takes the answer back into the queue and clears this.
    /// </summary>
    public DateTime? EvaluationGaveUpAtUtc { get; private set; }

    /// <summary>Whether the answer is queued for evaluation and has not had its verdict yet.</summary>
    public bool IsAwaitingEvaluation => EvaluationRequestedAtUtc is not null;

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
        // A verdict is about the words that were typed, so a different answer has none. Only an open attempt changes answers, and evaluation
        // starts when the attempt ends, so this clears nothing in practice and keeps the two from ever disagreeing.
        ClearEvaluation();
    }

    /// <summary>Queues the answer for evaluation. Any earlier verdict stays in force until the new one replaces it.</summary>
    /// <param name="nowUtc">The current instant.</param>
    internal void RequestEvaluation(DateTime nowUtc)
    {
        EvaluationRequestedAtUtc = nowUtc;
        EvaluationGaveUpAtUtc = null;
        EvaluationNextAttemptAtUtc = null;
        EvaluationLeaseUntilUtc = null;
        EvaluationFailures = 0;
        EvaluationLastError = null;
    }

    /// <summary>Records the evaluator's verdict and takes the answer out of the queue.</summary>
    /// <param name="verdict">Right or wrong.</param>
    /// <param name="reason">The evaluator's reason; kept to <see cref="MaxEvaluationReasonLength"/> characters.</param>
    /// <param name="model">The model that decided.</param>
    /// <param name="nowUtc">The current instant.</param>
    internal void RecordEvaluation(TypedAnswerVerdict verdict, string? reason, string? model, DateTime nowUtc)
    {
        TextVerdict = verdict;
        EvaluationReason = Shorten(reason, MaxEvaluationReasonLength);
        EvaluationModel = Shorten(model, 100);
        EvaluatedAtUtc = nowUtc;
        EvaluationGaveUpAtUtc = null;
        EvaluationRequestedAtUtc = null;
        EvaluationNextAttemptAtUtc = null;
        EvaluationLeaseUntilUtc = null;
        EvaluationFailures = 0;
        EvaluationLastError = null;
    }

    /// <summary>
    /// Stops waiting for a verdict: the answer leaves the queue with none, so it is marked by the accepted answers alone, and says so. The
    /// failures and the last error stay, so staff can see why the evaluator never answered.
    /// </summary>
    /// <param name="reason">What to tell staff; kept to <see cref="MaxEvaluationReasonLength"/> characters.</param>
    /// <param name="nowUtc">The current instant.</param>
    internal void GiveUpEvaluation(string reason, DateTime nowUtc)
    {
        // An earlier verdict was given against a key that may since have changed, and "marked by the accepted answers alone" has to be true.
        TextVerdict = null;
        EvaluationReason = Shorten(reason, MaxEvaluationReasonLength);
        EvaluationModel = null;
        EvaluatedAtUtc = null;
        EvaluationGaveUpAtUtc = nowUtc;
        EvaluationRequestedAtUtc = null;
        EvaluationNextAttemptAtUtc = null;
        EvaluationLeaseUntilUtc = null;
    }

    /// <summary>Lets go of the answer without counting a failure, so another worker can take it at once. For a worker that is stopping with the answer in hand.</summary>
    internal void ReleaseEvaluation() => EvaluationLeaseUntilUtc = null;

    /// <summary>Records that asking failed, and when to try again. The answer stays in the queue: a result waits for its verdict.</summary>
    /// <param name="error">What went wrong, in a few words; never the answer's content.</param>
    /// <param name="nextAttemptAtUtc">The earliest instant to try again.</param>
    internal void RecordEvaluationFailure(string error, DateTime nextAttemptAtUtc)
    {
        EvaluationFailures++;
        EvaluationLastError = Shorten(error, MaxEvaluationErrorLength);
        EvaluationNextAttemptAtUtc = nextAttemptAtUtc;
        EvaluationLeaseUntilUtc = null;
    }

    private void ClearEvaluation()
    {
        TextVerdict = null;
        EvaluationReason = null;
        EvaluationModel = null;
        EvaluatedAtUtc = null;
        EvaluationGaveUpAtUtc = null;
        EvaluationRequestedAtUtc = null;
        EvaluationNextAttemptAtUtc = null;
        EvaluationLeaseUntilUtc = null;
        EvaluationFailures = 0;
        EvaluationLastError = null;
    }

    private static string? Shorten(string? text, int maxLength)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;

        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
