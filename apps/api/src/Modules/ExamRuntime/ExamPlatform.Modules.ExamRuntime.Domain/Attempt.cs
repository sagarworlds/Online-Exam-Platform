using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain;

/// <summary>
/// One candidate's sitting of one exam (FR-16 to FR-21): when it started, the hard deadline the server
/// holds them to, the answers saved so far, and, once it is over, the score. The deadline is fixed at the
/// start, on the server, so a client clock cannot stretch the exam.
/// </summary>
/// <remarks>
/// A candidate has one attempt per exam, and one more for each <see cref="ExtraAttemptGrant"/> an administrator gives them.
/// Attempts are numbered from 1 and the store keeps (exam, candidate, number) unique, so two parallel "start" calls cannot
/// both take the same number. Resuming an open attempt and starting the next one are the same call; see the start handler.
/// </remarks>
public sealed class Attempt : AggregateRoot
{
    private readonly List<AttemptAnswer> _answers = [];
    private readonly List<AttemptMark> _marks = [];
    private readonly List<AttemptQuestion> _paper = [];
    private readonly List<AttemptResultRevision> _revisions = [];
    private readonly List<AttemptFocusViolation> _focusViolations = [];

    /// <summary>The exam being taken.</summary>
    public Guid ExamId { get; private set; }

    /// <summary>The candidate taking it; the signed-in user's id.</summary>
    public Guid CandidateId { get; private set; }

    /// <summary>Which attempt this is for the candidate at the exam, from 1.</summary>
    public int Number { get; private set; }

    /// <summary>When the candidate started.</summary>
    public DateTime StartedAtUtc { get; private set; }

    /// <summary>When the attempt must end, however the candidate's own clock reads.</summary>
    public DateTime DeadlineUtc { get; private set; }

    /// <summary>
    /// When the candidate confirmed they had read the exam's instructions (FR-17), as the attempt began. Null for an attempt that began
    /// before instructions were acknowledged; kept so a dispute can show the candidate was told the rules.
    /// </summary>
    public DateTime? InstructionsAcknowledgedAtUtc { get; private set; }

    /// <summary>Whether the attempt is still open.</summary>
    public AttemptStatus Status { get; private set; }

    /// <summary>When the attempt ended; never later than <see cref="DeadlineUtc"/>.</summary>
    public DateTime? SubmittedAtUtc { get; private set; }

    /// <summary>Whether the attempt ended because time ran out rather than because the candidate submitted it.</summary>
    public bool AutoSubmitted { get; private set; }

    /// <summary>
    /// Whether the attempt was ended by the server because the candidate left the exam page more often than the exam allows (FR-22),
    /// rather than by the candidate or by time running out. Such an attempt is also <see cref="AutoSubmitted"/>.
    /// </summary>
    public bool EndedByViolations { get; private set; }

    /// <summary>The marks scored, once submitted.</summary>
    public decimal? Score { get; private set; }

    /// <summary>The marks available, once submitted.</summary>
    public decimal? MaxScore { get; private set; }

    /// <summary>
    /// Position (the section's <c>Order</c>, from 1) of the section the candidate is in. It only moves forward, which is
    /// what makes a locked section stay left; exams without section lock never read it.
    /// </summary>
    public int ActiveSectionOrder { get; private set; } = 1;

    /// <summary>The answers saved so far, at most one per question (an answer to a multiple-answer question holds several options).</summary>
    public IReadOnlyList<AttemptAnswer> Answers => _answers.AsReadOnly();

    /// <summary>The questions the candidate has marked for review, at most one mark per question. Marks never affect the score.</summary>
    public IReadOnlyList<AttemptMark> Marks => _marks.AsReadOnly();

    /// <summary>
    /// The questions drawn for this attempt, section by section. Empty when the exam has no draw rules, in which case the exam's
    /// fixed list is the paper.
    /// </summary>
    public IReadOnlyList<AttemptQuestion> Paper => _paper.AsReadOnly();

    /// <summary>
    /// How this attempt's score has changed since it was first submitted (FR-31), oldest first; empty for a result that
    /// has never been revised. See <see cref="ReviseScore"/>.
    /// </summary>
    public IReadOnlyList<AttemptResultRevision> Revisions => _revisions.AsReadOnly();

    /// <summary>
    /// Every time the candidate left the exam page, oldest first (FR-22). Only filled when the exam watches for it; see
    /// <see cref="RecordFocusViolation"/>.
    /// </summary>
    public IReadOnlyList<AttemptFocusViolation> FocusViolations => _focusViolations.AsReadOnly();

    // For EF Core.
    private Attempt() : base(Guid.Empty)
    {
    }

    private Attempt(Guid id, Guid examId, Guid candidateId, int number, DateTime startedAtUtc, DateTime deadlineUtc) : base(id)
    {
        ExamId = examId;
        CandidateId = candidateId;
        Number = number;
        StartedAtUtc = startedAtUtc;
        DeadlineUtc = deadlineUtc;
        Status = AttemptStatus.InProgress;
    }

    /// <summary>Begins an attempt.</summary>
    /// <param name="examId">The exam being taken.</param>
    /// <param name="candidateId">The candidate.</param>
    /// <param name="number">Which attempt this is for them at the exam: the number they have already started, plus one.</param>
    /// <param name="startedAtUtc">The current instant.</param>
    /// <param name="deadlineUtc">When the attempt must end; the caller works it out from the exam's rules.</param>
    /// <exception cref="InvalidAttemptError">The number is not positive, or the deadline is not after the start.</exception>
    public static Attempt Start(Guid examId, Guid candidateId, int number, DateTime startedAtUtc, DateTime deadlineUtc)
    {
        if (number < 1)
            throw new InvalidAttemptError("An attempt is numbered from 1.");

        if (deadlineUtc <= startedAtUtc)
            throw new InvalidAttemptError("An attempt must end after it starts.");

        return new Attempt(Guid.NewGuid(), examId, candidateId, number, startedAtUtc, deadlineUtc);
    }

    /// <summary>Records that the candidate acknowledged the instructions as they started. Done once, when the attempt is created.</summary>
    /// <param name="acknowledgedAtUtc">The current instant.</param>
    /// <exception cref="InvalidAttemptError">The attempt already records an acknowledgment.</exception>
    public void AcknowledgeInstructions(DateTime acknowledgedAtUtc)
    {
        if (InstructionsAcknowledgedAtUtc is not null)
            throw new InvalidAttemptError("The instructions were already acknowledged for this attempt.");

        InstructionsAcknowledgedAtUtc = acknowledgedAtUtc;
    }

    /// <summary>Fixes the questions this attempt consists of. Done once, as the attempt starts.</summary>
    /// <param name="questions">The questions in the order they were drawn, each with its section; within a section they keep that order.</param>
    /// <exception cref="InvalidAttemptError">The attempt already has a paper, or a question appears twice.</exception>
    public void SetPaper(IEnumerable<(Guid SectionId, Guid QuestionId)> questions)
    {
        if (_paper.Count > 0)
            throw new InvalidAttemptError("This attempt already has its paper.");

        var seen = new HashSet<Guid>();
        var perSection = new Dictionary<Guid, int>();
        foreach (var (sectionId, questionId) in questions)
        {
            // A repeated question would share one answer between two places on the paper.
            if (!seen.Add(questionId))
                throw new InvalidAttemptError("A question cannot be on the paper twice.");

            var order = perSection.GetValueOrDefault(sectionId) + 1;
            perSection[sectionId] = order;
            _paper.Add(new AttemptQuestion(Id, sectionId, order, questionId));
        }
    }

    /// <summary>Whether time has run out at <paramref name="nowUtc"/>.</summary>
    /// <param name="nowUtc">The current instant.</param>
    public bool IsExpired(DateTime nowUtc) => nowUtc >= DeadlineUtc;

    /// <summary>
    /// Saves the option a candidate chose for a single-answer question, replacing an earlier choice for the same question.
    /// The caller has already checked that the question belongs to the exam and the option to the question.
    /// </summary>
    /// <param name="questionId">The question answered.</param>
    /// <param name="selectedOptionId">The option chosen.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <exception cref="AttemptNotInProgressError">The attempt is already submitted.</exception>
    /// <exception cref="AttemptTimeExpiredError">The deadline has passed.</exception>
    public void RecordAnswer(Guid questionId, Guid selectedOptionId, DateTime nowUtc) =>
        RecordAnswer(questionId, [selectedOptionId], nowUtc);

    /// <summary>
    /// Saves the options a candidate chose for a question, replacing an earlier choice for the same question. The set is the answer:
    /// saving again with different options replaces them all. The caller has already checked that the question belongs to the exam,
    /// that every option belongs to the question, and that a single-answer question got exactly one.
    /// </summary>
    /// <param name="questionId">The question answered.</param>
    /// <param name="selectedOptionIds">The options chosen; repeats are ignored, and at least one is needed (to take an answer back, clear it).</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <exception cref="InvalidAttemptError">No option was given.</exception>
    /// <exception cref="AttemptNotInProgressError">The attempt is already submitted.</exception>
    /// <exception cref="AttemptTimeExpiredError">The deadline has passed.</exception>
    public void RecordAnswer(Guid questionId, IReadOnlyCollection<Guid> selectedOptionIds, DateTime nowUtc)
    {
        EnsureOpen(nowUtc);

        var chosen = selectedOptionIds.Distinct().ToList();
        if (chosen.Count == 0)
            throw new InvalidAttemptError("An answer needs at least one option; to take an answer back, clear it.");

        var existing = _answers.FirstOrDefault(a => a.QuestionId == questionId);
        if (existing is null)
            _answers.Add(new AttemptAnswer(Id, questionId, chosen, nowUtc));
        else
            existing.Change(chosen, nowUtc);
    }

    /// <summary>
    /// Takes back the option a candidate chose for a question, so it counts as unanswered again ("clear response", FR-18).
    /// Clearing a question with no answer does nothing: the candidate asked for "no answer" and that is what they have.
    /// </summary>
    /// <remarks>
    /// The answer is removed rather than kept with an empty choice, so everything that reads answers keeps meaning
    /// "a candidate chose this option": a cleared question is neither scored nor treated as answered.
    /// </remarks>
    /// <param name="questionId">The question to clear.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <exception cref="AttemptNotInProgressError">The attempt is already submitted.</exception>
    /// <exception cref="AttemptTimeExpiredError">The deadline has passed.</exception>
    public void ClearAnswer(Guid questionId, DateTime nowUtc)
    {
        EnsureOpen(nowUtc);

        var existing = _answers.FirstOrDefault(a => a.QuestionId == questionId);
        if (existing is not null)
            _answers.Remove(existing);
    }

    /// <summary>
    /// Marks a question for review, or takes the mark off ("mark for review", FR-18). Safe to repeat: marking a marked
    /// question, or unmarking an unmarked one, changes nothing. The caller has already checked that the question belongs to the exam.
    /// </summary>
    /// <param name="questionId">The question.</param>
    /// <param name="marked">Whether it should be marked.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <exception cref="AttemptNotInProgressError">The attempt is already submitted.</exception>
    /// <exception cref="AttemptTimeExpiredError">The deadline has passed.</exception>
    public void SetMarked(Guid questionId, bool marked, DateTime nowUtc)
    {
        EnsureOpen(nowUtc);

        var existing = _marks.FirstOrDefault(m => m.QuestionId == questionId);
        if (marked && existing is null)
            _marks.Add(new AttemptMark(Id, questionId, nowUtc));
        else if (!marked && existing is not null)
            _marks.Remove(existing);
    }

    /// <summary>
    /// Moves the candidate on to a later section. The position is stored on the server so a locked section stays left
    /// however the page is reloaded or the request is replayed. Moving to the section already open does nothing.
    /// </summary>
    /// <param name="sectionOrder">The <c>Order</c> of the section to move to.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <exception cref="AttemptNotInProgressError">The attempt is already submitted.</exception>
    /// <exception cref="AttemptTimeExpiredError">The deadline has passed.</exception>
    /// <exception cref="SectionLockedError">The section is before the one the candidate is in.</exception>
    public void MoveToSection(int sectionOrder, DateTime nowUtc)
    {
        EnsureOpen(nowUtc);

        if (sectionOrder < ActiveSectionOrder)
            throw new SectionLockedError();

        ActiveSectionOrder = sectionOrder;
    }

    /// <summary>
    /// Records that the candidate left the exam page (FR-22). The caller decides, from the exam's limit, whether this was the one that
    /// ends the attempt; the attempt only keeps count.
    /// </summary>
    /// <param name="kind">How they left it.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <returns>How many times the candidate has now left the page during this attempt, this one included.</returns>
    /// <exception cref="AttemptNotInProgressError">The attempt is already submitted.</exception>
    /// <exception cref="AttemptTimeExpiredError">The deadline has passed.</exception>
    public int RecordFocusViolation(FocusViolationKind kind, DateTime nowUtc)
    {
        EnsureOpen(nowUtc);

        _focusViolations.Add(new AttemptFocusViolation(Id, kind, nowUtc));
        return _focusViolations.Count;
    }

    /// <summary>
    /// Ends the attempt with its score. Submitting an attempt whose time has run out is allowed (it is how an
    /// abandoned attempt gets closed) and is recorded as an automatic submission at the deadline.
    /// </summary>
    /// <param name="nowUtc">The current instant.</param>
    /// <param name="score">The marks scored; computed by the caller from the answer key.</param>
    /// <param name="maxScore">The marks available.</param>
    /// <param name="endedByViolations">Whether the server is ending the attempt because the candidate left the exam page too often (FR-22).</param>
    /// <exception cref="AttemptNotInProgressError">The attempt is already submitted.</exception>
    public void Submit(DateTime nowUtc, decimal score, decimal maxScore, bool endedByViolations = false)
    {
        if (Status != AttemptStatus.InProgress)
            throw new AttemptNotInProgressError();

        // Ended by the server either way: time ran out, or the violation limit was reached.
        AutoSubmitted = IsExpired(nowUtc) || endedByViolations;
        EndedByViolations = endedByViolations;
        // Never later than the deadline: an attempt picked up and closed hours afterwards still records
        // that the candidate stopped when time ran out.
        SubmittedAtUtc = IsExpired(nowUtc) ? DeadlineUtc : nowUtc;
        Score = score;
        MaxScore = maxScore;
        Status = AttemptStatus.Submitted;
    }

    /// <summary>
    /// Recomputes this submitted attempt's score, most often because staff corrected a question's answer key (FR-31).
    /// Records the change as a <see cref="AttemptResultRevision"/> so the candidate can see their result moved and why,
    /// rather than it silently becoming a different number than the one they already saw.
    /// </summary>
    /// <param name="newScore">The marks scored, recomputed under the corrected answer key.</param>
    /// <param name="newMaxScore">The marks available, recomputed the same way.</param>
    /// <param name="reason">Why the score changed, shown to the candidate.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <returns><see langword="true"/> when the score actually changed; <see langword="false"/> when it did not (a no-op, not an error).</returns>
    /// <exception cref="AttemptNotSubmittedError">The attempt has not been submitted yet, so it has no score to revise.</exception>
    public bool ReviseScore(decimal newScore, decimal newMaxScore, string reason, DateTime nowUtc)
    {
        if (Status != AttemptStatus.Submitted)
            throw new AttemptNotSubmittedError();

        if (Score == newScore && MaxScore == newMaxScore)
            return false;

        _revisions.Add(new AttemptResultRevision(Id, Score!.Value, MaxScore!.Value, newScore, newMaxScore, reason, nowUtc));
        Score = newScore;
        MaxScore = newMaxScore;
        return true;
    }

    /// <summary>The one rule every change to an open attempt shares: it must still be open, and its time must not have run out.</summary>
    private void EnsureOpen(DateTime nowUtc)
    {
        if (Status != AttemptStatus.InProgress)
            throw new AttemptNotInProgressError();
        if (IsExpired(nowUtc))
            throw new AttemptTimeExpiredError();
    }
}
