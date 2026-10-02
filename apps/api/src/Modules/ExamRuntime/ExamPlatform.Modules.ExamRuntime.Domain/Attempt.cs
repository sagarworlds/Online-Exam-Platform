using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain;

/// <summary>
/// One candidate's sitting of one exam (FR-16 to FR-21): when it started, the hard deadline the server
/// holds them to, the answers saved so far, and, once it is over, the score. The deadline is fixed at the
/// start, on the server, so a client clock cannot stretch the exam.
/// </summary>
/// <remarks>
/// A candidate has at most one attempt per exam (the store enforces it), so "resume" and "start again"
/// are the same call and there are no retakes in this first cut.
/// </remarks>
public sealed class Attempt : AggregateRoot
{
    private readonly List<AttemptAnswer> _answers = [];

    /// <summary>The exam being taken.</summary>
    public Guid ExamId { get; private set; }

    /// <summary>The candidate taking it; the signed-in user's id.</summary>
    public Guid CandidateId { get; private set; }

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

    // For EF Core.
    private Attempt() : base(Guid.Empty)
    {
    }

    private Attempt(Guid id, Guid examId, Guid candidateId, DateTime startedAtUtc, DateTime deadlineUtc) : base(id)
    {
        ExamId = examId;
        CandidateId = candidateId;
        StartedAtUtc = startedAtUtc;
        DeadlineUtc = deadlineUtc;
        Status = AttemptStatus.InProgress;
    }

    /// <summary>Begins an attempt.</summary>
    /// <param name="examId">The exam being taken.</param>
    /// <param name="candidateId">The candidate.</param>
    /// <param name="startedAtUtc">The current instant.</param>
    /// <param name="deadlineUtc">When the attempt must end; the caller works it out from the exam's rules.</param>
    /// <exception cref="InvalidAttemptError">The deadline is not after the start.</exception>
    public static Attempt Start(Guid examId, Guid candidateId, DateTime startedAtUtc, DateTime deadlineUtc)
    {
        if (deadlineUtc <= startedAtUtc)
            throw new InvalidAttemptError("An attempt must end after it starts.");

        return new Attempt(Guid.NewGuid(), examId, candidateId, startedAtUtc, deadlineUtc);
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
        if (Status != AttemptStatus.InProgress)
            throw new AttemptNotInProgressError();
        if (IsExpired(nowUtc))
            throw new AttemptTimeExpiredError();

        var existing = _answers.FirstOrDefault(a => a.QuestionId == questionId);
        if (existing is null)
            _answers.Add(new AttemptAnswer(Id, questionId, selectedOptionId, nowUtc));
        else
            existing.Change(selectedOptionId, nowUtc);
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
}
