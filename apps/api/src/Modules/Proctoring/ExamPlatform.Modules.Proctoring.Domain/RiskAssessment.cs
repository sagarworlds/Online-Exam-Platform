using ExamPlatform.Modules.Proctoring.Domain.Exceptions;
using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Proctoring.Domain;

/// <summary>
/// The risk score of one attempt (FR-27), its readings, and what a human made of it. A flagged assessment is in the review queue until a
/// reviewer marks it reviewed or dismisses it.
/// </summary>
/// <remarks>
/// Nothing here acts on the candidate: no penalty, no block, no invalidation, no message. The only changes a person can make are the two
/// decisions below, and both are recorded with who made them and when. A reviewer's decision is never overwritten by a later scan.
/// </remarks>
public sealed class RiskAssessment : AggregateRoot
{
    private readonly List<RiskSignalReading> _signals = [];

    // For EF Core.
    private RiskAssessment() : base(Guid.Empty)
    {
    }

    private RiskAssessment(Guid id) : base(id)
    {
    }

    /// <summary>The exam the attempt was sat at.</summary>
    public Guid ExamId { get; private set; }

    /// <summary>The attempt scored. An attempt has at most one assessment.</summary>
    public Guid AttemptId { get; private set; }

    /// <summary>The candidate who sat the attempt.</summary>
    public Guid CandidateId { get; private set; }

    /// <summary>Which attempt it was for the candidate, from 1, when the score was worked out.</summary>
    public int AttemptNumber { get; private set; }

    /// <summary>The points added up over the raised signals.</summary>
    public int Score { get; private set; }

    /// <summary>The most points any attempt could score under the policy the score was worked out with.</summary>
    public int MaxScore { get; private set; }

    /// <summary>Whether the score reached the flag threshold: only flagged assessments are in the review queue.</summary>
    public bool Flagged { get; private set; }

    /// <summary>Where the reviewer's work on it stands.</summary>
    public RiskFlagStatus Status { get; private set; }

    /// <summary>When the score was last worked out, by the server's clock.</summary>
    public DateTime ComputedAtUtc { get; private set; }

    /// <summary>Every signal as it was judged, so a reviewer can see why the attempt was flagged.</summary>
    public IReadOnlyList<RiskSignalReading> Signals => _signals.AsReadOnly();

    /// <summary>The reviewer's note: required for a dismissal, optional for a review.</summary>
    public string? DecisionNote { get; private set; }

    /// <summary>The reviewer who decided, taken from their token; null while open.</summary>
    public Guid? DecidedByUserId { get; private set; }

    /// <summary>When the decision was made; null while open.</summary>
    public DateTime? DecidedAtUtc { get; private set; }

    /// <summary>The longest note a reviewer can write.</summary>
    public const int MaxNoteLength = 500;

    /// <summary>Creates the assessment of an attempt that has just been scored.</summary>
    /// <param name="examId">The exam.</param>
    /// <param name="input">The facts the score was worked out from.</param>
    /// <param name="outcome">The score.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <returns>An open assessment.</returns>
    public static RiskAssessment Create(Guid examId, AttemptRiskInputs input, RiskOutcome outcome, DateTime nowUtc)
    {
        var assessment = new RiskAssessment(Guid.NewGuid())
        {
            ExamId = examId,
            AttemptId = input.AttemptId,
            CandidateId = input.CandidateId,
            AttemptNumber = input.AttemptNumber,
            Status = RiskFlagStatus.Open,
        };
        assessment.ApplyOutcome(outcome, nowUtc);
        return assessment;
    }

    /// <summary>
    /// Replaces the score with a fresh one. Only an open assessment can be rescored: once a reviewer has decided, the score they decided on
    /// is kept as it was, so the record of what they saw does not change under them.
    /// </summary>
    /// <param name="input">The facts the new score was worked out from.</param>
    /// <param name="outcome">The new score.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <exception cref="RiskFlagAlreadyDecidedError">A reviewer has already decided on this assessment.</exception>
    public void Rescore(AttemptRiskInputs input, RiskOutcome outcome, DateTime nowUtc)
    {
        EnsureOpen();
        AttemptNumber = input.AttemptNumber;
        ApplyOutcome(outcome, nowUtc);
    }

    /// <summary>Marks a flagged attempt as reviewed: the reviewer looked at it and judged that it needs no further action.</summary>
    /// <param name="byUserId">The reviewer, from their token.</param>
    /// <param name="note">An optional note, up to <see cref="MaxNoteLength"/> characters.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <exception cref="RiskFlagNotRaisedError">The attempt is not flagged, so there is nothing in the queue to review.</exception>
    /// <exception cref="RiskFlagAlreadyDecidedError">A reviewer has already decided on it.</exception>
    /// <exception cref="InvalidRiskDecisionError">The note is longer than <see cref="MaxNoteLength"/>.</exception>
    public void MarkReviewed(Guid byUserId, string? note, DateTime nowUtc)
    {
        EnsureFlaggedAndOpen();
        Decide(RiskFlagStatus.Reviewed, byUserId, CleanNote(note, required: false), nowUtc);
    }

    /// <summary>
    /// Dismisses a flag. A dismissal must say why, so that the next person to read the record knows what the reviewer judged.
    /// </summary>
    /// <param name="byUserId">The reviewer, from their token.</param>
    /// <param name="note">Why the flag was dismissed; required, up to <see cref="MaxNoteLength"/> characters.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <exception cref="RiskFlagNotRaisedError">The attempt is not flagged, so there is nothing in the queue to dismiss.</exception>
    /// <exception cref="RiskFlagAlreadyDecidedError">A reviewer has already decided on it.</exception>
    /// <exception cref="InvalidRiskDecisionError">The note is blank or longer than <see cref="MaxNoteLength"/>.</exception>
    public void Dismiss(Guid byUserId, string? note, DateTime nowUtc)
    {
        EnsureFlaggedAndOpen();
        Decide(RiskFlagStatus.Dismissed, byUserId, CleanNote(note, required: true), nowUtc);
    }

    private void ApplyOutcome(RiskOutcome outcome, DateTime nowUtc)
    {
        Score = outcome.Score;
        MaxScore = outcome.MaxScore;
        Flagged = outcome.Flagged;
        ComputedAtUtc = nowUtc;
        _signals.Clear();
        _signals.AddRange(outcome.Readings);
    }

    private void Decide(RiskFlagStatus decision, Guid byUserId, string? note, DateTime nowUtc)
    {
        Status = decision;
        DecidedByUserId = byUserId;
        DecidedAtUtc = nowUtc;
        DecisionNote = note;
    }

    private void EnsureOpen()
    {
        if (Status != RiskFlagStatus.Open)
            throw new RiskFlagAlreadyDecidedError();
    }

    private void EnsureFlaggedAndOpen()
    {
        if (!Flagged)
            throw new RiskFlagNotRaisedError();
        EnsureOpen();
    }

    // Trimmed, so that a note of only spaces counts as no note at all, and a dismissal without a real reason is refused.
    private static string? CleanNote(string? note, bool required)
    {
        var text = note?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            if (required)
                throw new InvalidRiskDecisionError("A dismissal needs a note saying why the flag was dismissed.");
            return null;
        }

        if (text.Length > MaxNoteLength)
            throw new InvalidRiskDecisionError($"A note can be at most {MaxNoteLength} characters.");

        return text;
    }
}
