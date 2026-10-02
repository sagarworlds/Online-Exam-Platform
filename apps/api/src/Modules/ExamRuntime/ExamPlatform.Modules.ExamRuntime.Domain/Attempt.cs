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

    /// <summary>Whether the attempt is still open.</summary>
    public AttemptStatus Status { get; private set; }

    /// <summary>When the attempt ended; never later than <see cref="DeadlineUtc"/>.</summary>
    public DateTime? SubmittedAtUtc { get; private set; }

    /// <summary>Whether the attempt ended because time ran out rather than because the candidate submitted it.</summary>
    public bool AutoSubmitted { get; private set; }

    /// <summary>The marks scored, once submitted.</summary>
    public decimal? Score { get; private set; }

    /// <summary>The marks available, once submitted.</summary>
    public decimal? MaxScore { get; private set; }

    /// <summary>The answers saved so far, at most one per question.</summary>
    public IReadOnlyList<AttemptAnswer> Answers => _answers.AsReadOnly();

    /// <summary>The questions the candidate has marked for review, at most one mark per question. Marks never affect the score.</summary>
    public IReadOnlyList<AttemptMark> Marks => _marks.AsReadOnly();

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

    /// <summary>Whether time has run out at <paramref name="nowUtc"/>.</summary>
    /// <param name="nowUtc">The current instant.</param>
    public bool IsExpired(DateTime nowUtc) => nowUtc >= DeadlineUtc;

    /// <summary>
    /// Saves the option a candidate chose for a question, replacing an earlier choice for the same question.
    /// The caller has already checked that the question belongs to the exam and the option to the question.
    /// </summary>
    /// <param name="questionId">The question answered.</param>
    /// <param name="selectedOptionId">The option chosen.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <exception cref="AttemptNotInProgressError">The attempt is already submitted.</exception>
    /// <exception cref="AttemptTimeExpiredError">The deadline has passed.</exception>
    public void RecordAnswer(Guid questionId, Guid selectedOptionId, DateTime nowUtc)
    {
        EnsureOpen(nowUtc);

        var existing = _answers.FirstOrDefault(a => a.QuestionId == questionId);
        if (existing is null)
            _answers.Add(new AttemptAnswer(Id, questionId, selectedOptionId, nowUtc));
        else
            existing.Change(selectedOptionId, nowUtc);
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
    /// Ends the attempt with its score. Submitting an attempt whose time has run out is allowed (it is how an
    /// abandoned attempt gets closed) and is recorded as an automatic submission at the deadline.
    /// </summary>
    /// <param name="nowUtc">The current instant.</param>
    /// <param name="score">The marks scored; computed by the caller from the answer key.</param>
    /// <param name="maxScore">The marks available.</param>
    /// <exception cref="AttemptNotInProgressError">The attempt is already submitted.</exception>
    public void Submit(DateTime nowUtc, decimal score, decimal maxScore)
    {
        if (Status != AttemptStatus.InProgress)
            throw new AttemptNotInProgressError();

        AutoSubmitted = IsExpired(nowUtc);
        // Never later than the deadline: an attempt picked up and closed hours afterwards still records
        // that the candidate stopped when time ran out.
        SubmittedAtUtc = AutoSubmitted ? DeadlineUtc : nowUtc;
        Score = score;
        MaxScore = maxScore;
        Status = AttemptStatus.Submitted;
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
