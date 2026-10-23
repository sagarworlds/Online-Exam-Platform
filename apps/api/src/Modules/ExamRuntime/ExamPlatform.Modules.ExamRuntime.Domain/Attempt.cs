using ExamPlatform.Modules.ExamRuntime.Domain.Events;
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
    private readonly List<AttemptQuestionTime> _questionTimes = [];
    private readonly List<AttemptAnswerSequence> _answerSequences = [];
    private readonly List<AttemptQuestion> _paper = [];
    private readonly List<AttemptResultRevision> _revisions = [];
    private readonly List<AttemptFocusViolation> _focusViolations = [];
    private readonly List<AttemptWarning> _warnings = [];
    private readonly List<AttemptClientSighting> _clientSightings = [];

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

    /// <summary>
    /// The notice about what is turned off, recorded and watched (FR-46) that the candidate acknowledged as they started, exactly as it was
    /// shown. Kept so a dispute can show what they were told, even after the exam's settings or the wording later change. Null for an
    /// attempt that began before the notice was kept.
    /// </summary>
    public string? AcknowledgedNotice { get; private set; }

    /// <summary>The longest notice kept; far longer than any the platform writes.</summary>
    public const int MaxNoticeLength = 2000;

    /// <summary>
    /// The extra time, in seconds, this attempt carries because of the candidate's accommodation (FR-49), already part of
    /// <see cref="DeadlineUtc"/>. Kept on the attempt so what applied to it is on record whatever staff change later, and so it is never
    /// lowered while the candidate is sitting.
    /// </summary>
    public int AccommodationExtraSeconds { get; private set; }

    /// <summary>Whether the candidate may use a reader or scribe at this attempt (FR-49).</summary>
    public bool AccommodationReaderScribe { get; private set; }

    /// <summary>The alternate formats of the exam page this attempt is delivered in (FR-49), from <see cref="AccommodationFormat.All"/>.</summary>
    public string[] AccommodationFormats { get; private set; } = [];

    /// <summary>Whether any accommodation applies to this attempt.</summary>
    public bool IsAccommodated => AccommodationExtraSeconds > 0 || AccommodationReaderScribe || AccommodationFormats.Length > 0;

    /// <summary>Whether the attempt is still open.</summary>
    public AttemptStatus Status { get; private set; }

    /// <summary>When the attempt ended; never later than <see cref="DeadlineUtc"/>.</summary>
    public DateTime? SubmittedAtUtc { get; private set; }

    /// <summary>Whether the attempt ended because time ran out rather than because the candidate submitted it.</summary>
    public bool AutoSubmitted { get; private set; }

    /// <summary>
    /// Whether time ran out before the candidate submitted, so the clock ended the attempt. Violations and administrators also end an attempt
    /// and set <see cref="AutoSubmitted"/>, so this is the reading the item analysis needs for drop-off (FR-37).
    /// </summary>
    public bool EndedByClock => AutoSubmitted && !EndedByViolations && TerminatedByUserId is null;

    /// <summary>
    /// Whether the attempt was ended by the server because the candidate left the exam page more often than the exam allows (FR-22),
    /// rather than by the candidate or by time running out. Such an attempt is also <see cref="AutoSubmitted"/>.
    /// </summary>
    public bool EndedByViolations { get; private set; }

    /// <summary>
    /// When an administrator paused the attempt (FR-29), or null while it runs. A paused attempt cannot be answered, its deadline does
    /// not pass, and <see cref="Resume"/> moves the deadline later by the time it was paused.
    /// </summary>
    public DateTime? PausedAtUtc { get; private set; }

    /// <summary>The administrator who ended the attempt early (FR-29), or null when nobody did.</summary>
    public Guid? TerminatedByUserId { get; private set; }

    /// <summary>Why the administrator ended it, shown to the candidate; null unless <see cref="TerminatedByUserId"/> is set.</summary>
    public string? TerminationReason { get; private set; }

    /// <summary>When an administrator invalidated the result (FR-29), or null while it stands.</summary>
    public DateTime? InvalidatedAtUtc { get; private set; }

    /// <summary>The administrator who invalidated it.</summary>
    public Guid? InvalidatedByUserId { get; private set; }

    /// <summary>Why the result was invalidated, shown to the candidate.</summary>
    public string? InvalidationReason { get; private set; }

    /// <summary>Whether an administrator invalidated the result, so it carries no score or review for the candidate and must not be counted.</summary>
    public bool IsInvalidated => InvalidatedAtUtc is not null;

    /// <summary>Whether an administrator ended the attempt early.</summary>
    public bool IsTerminated => TerminatedByUserId is not null;

    /// <summary>The marks scored, once submitted.</summary>
    public decimal? Score { get; private set; }

    /// <summary>The marks available, once submitted.</summary>
    public decimal? MaxScore { get; private set; }

    /// <summary>
    /// Why the score will be worked out again, while typed answers are being checked again after a question's answer key was corrected
    /// (FR-31); null otherwise. When the checking is done the score is revised with this as the reason, so the candidate sees why it moved.
    /// </summary>
    public string? PendingRevisionReason { get; private set; }

    /// <summary>The longest revision reason kept, the length an <see cref="AttemptResultRevision"/> stores.</summary>
    public const int MaxRevisionReasonLength = 500;

    /// <summary>Whether any typed answer is queued for the AI evaluator and has not had its verdict yet.</summary>
    public bool IsAwaitingEvaluation => _answers.Any(a => a.IsAwaitingEvaluation);

    /// <summary>The typed answers queued for the AI evaluator that have not had their verdict yet.</summary>
    public IEnumerable<AttemptAnswer> AnswersAwaitingEvaluation => _answers.Where(a => a.IsAwaitingEvaluation);

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
    /// How long the candidate has spent on each question, in seconds, as the page has measured it; a question the page never reported on is
    /// absent. See <see cref="RecordQuestionTime"/>.
    /// </summary>
    public IReadOnlyList<AttemptQuestionTime> QuestionTimes => _questionTimes.AsReadOnly();

    /// <summary>
    /// The highest sequence number applied to each question's answer; a question nothing was ever written to is absent. A write to an answer
    /// is applied only when its number is higher (NFR-4, ADR 0004). See <see cref="AttemptAnswerSequence"/>.
    /// </summary>
    public IReadOnlyList<AttemptAnswerSequence> AnswerSequences => _answerSequences.AsReadOnly();

    /// <summary>
    /// The questions drawn for this attempt, section by section. Empty when the exam has no draw rules, in which case the exam's
    /// fixed list is the paper.
    /// </summary>
    public IReadOnlyList<AttemptQuestion> Paper => _paper.AsReadOnly();

    /// <summary>
    /// The version of each question this attempt was sitting (FR-7), by question id, recorded as it started. Reading the question at
    /// that version, not its current content, is what keeps an edit made afterwards from changing what the candidate saw or how it
    /// is marked. Empty for an attempt made before versions were recorded, which reads the questions as they are now.
    /// </summary>
    public IReadOnlyDictionary<Guid, int> QuestionVersions { get; private set; } = new Dictionary<Guid, int>();

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

    /// <summary>
    /// Where this attempt was sat from, oldest first (FR-26): the first row is where it began, and each later one is a change of address
    /// or device. See <see cref="NoteClient"/>.
    /// </summary>
    public IReadOnlyList<AttemptClientSighting> ClientSightings => _clientSightings.AsReadOnly();

    /// <summary>How many different device signatures this attempt was seen on; more than one means the account was used from another device.</summary>
    public int DeviceCount => _clientSightings.Select(s => s.DeviceFingerprint).Where(f => f is not null).Distinct().Count();

    /// <summary>How many times the attempt was seen from a different address or device than before (FR-26).</summary>
    public int ClientChanges => _clientSightings.Count(s => s.Reason == ClientSightingReason.Changed);

    /// <summary>The warnings administrators sent during this attempt, oldest first (FR-29).</summary>
    public IReadOnlyList<AttemptWarning> Warnings => _warnings.AsReadOnly();

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

    /// <summary>
    /// Applies the candidate's accommodation to this attempt (FR-49): the extra time is added to the deadline, and the formats and the
    /// reader flag are recorded. Called as the attempt starts, and again when staff set an accommodation while it is in progress.
    /// </summary>
    /// <remarks>
    /// Extra time only ever grows during an attempt: a candidate who was told they have an hour and a half is never left with less because
    /// staff later lowered the figure; the lower figure applies to the attempts that follow. Only the time that is new is added, so
    /// applying the same accommodation twice changes nothing. Formats and the reader flag can change either way, as they take nothing away.
    /// </remarks>
    /// <param name="extraTimeSeconds">The extra time the accommodation gives.</param>
    /// <param name="readerScribe">Whether a reader or scribe is allowed.</param>
    /// <param name="formats">The alternate formats.</param>
    /// <exception cref="AttemptNotInProgressError">The attempt is already over.</exception>
    public void ApplyAccommodation(int extraTimeSeconds, bool readerScribe, IEnumerable<string> formats)
    {
        EnsureInProgress();

        var added = Math.Max(0, extraTimeSeconds - AccommodationExtraSeconds);
        var formatList = formats.ToArray();
        var changed = added > 0 || readerScribe != AccommodationReaderScribe || !formatList.OrderBy(f => f).SequenceEqual(AccommodationFormats.OrderBy(f => f));

        AccommodationExtraSeconds += added;
        DeadlineUtc = DeadlineUtc.AddSeconds(added);
        AccommodationReaderScribe = readerScribe;
        AccommodationFormats = formatList;

        if (changed)
            AddDomainEvent(new AttemptAccommodatedEvent(Id, ExamId, CandidateId, AccommodationExtraSeconds, added));
    }

    /// <summary>Records that the candidate acknowledged the instructions as they started. Done once, when the attempt is created.</summary>
    /// <param name="acknowledgedAtUtc">The current instant.</param>
    /// <param name="notice">The notice shown to the candidate, kept as acknowledged (FR-46); null when there was none.</param>
    /// <exception cref="InvalidAttemptError">The attempt already records an acknowledgment.</exception>
    public void AcknowledgeInstructions(DateTime acknowledgedAtUtc, string? notice = null)
    {
        if (InstructionsAcknowledgedAtUtc is not null)
            throw new InvalidAttemptError("The instructions were already acknowledged for this attempt.");

        InstructionsAcknowledgedAtUtc = acknowledgedAtUtc;
        AcknowledgedNotice = string.IsNullOrWhiteSpace(notice) ? null : notice.Length > MaxNoticeLength ? notice[..MaxNoticeLength] : notice;
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

    /// <summary>Records which version of each question this attempt is sitting. Done once, as the attempt starts.</summary>
    /// <param name="versions">The version number in force for each question of the attempt, by question id.</param>
    /// <exception cref="InvalidAttemptError">The attempt already has its versions.</exception>
    public void PinQuestionVersions(IReadOnlyDictionary<Guid, int> versions)
    {
        if (QuestionVersions.Count > 0)
            throw new InvalidAttemptError("This attempt already has its question versions.");

        QuestionVersions = new Dictionary<Guid, int>(versions);
    }

    /// <summary>
    /// Moves one question to a newer version, because staff corrected its answer key and this attempt is being rescored under the
    /// corrected key (FR-31). Nothing else about the attempt's questions changes.
    /// </summary>
    /// <param name="questionId">The question whose key was corrected.</param>
    /// <param name="versionNumber">The version that holds the corrected key.</param>
    public void RepinQuestion(Guid questionId, int versionNumber) =>
        QuestionVersions = new Dictionary<Guid, int>(QuestionVersions) { [questionId] = versionNumber };

    /// <summary>The version of a question this attempt is sitting, or null when none was recorded (read the current one).</summary>
    /// <param name="questionId">The question.</param>
    public int? QuestionVersionOf(Guid questionId) => QuestionVersions.TryGetValue(questionId, out var version) ? version : null;

    /// <summary>Whether time has run out at <paramref name="nowUtc"/>.</summary>
    /// <param name="nowUtc">The current instant.</param>
    public bool IsExpired(DateTime nowUtc) =>
        // A paused attempt's clock is stopped, so it cannot run out until it is resumed (FR-29).
        PausedAtUtc is null && nowUtc >= DeadlineUtc;

    /// <summary>
    /// Saves the option a candidate chose for a single-answer question, replacing an earlier choice for the same question.
    /// The caller has already checked that the question belongs to the exam and the option to the question.
    /// </summary>
    /// <param name="questionId">The question answered.</param>
    /// <param name="selectedOptionId">The option chosen.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <param name="sequence">The client's sequence number for this write, or null from a client that sends none; see <see cref="AttemptAnswerSequence"/>.</param>
    /// <exception cref="AttemptNotInProgressError">The attempt is already submitted.</exception>
    /// <exception cref="AttemptTimeExpiredError">The deadline has passed.</exception>
    /// <exception cref="InvalidAttemptError">The sequence number is outside the range a client may send.</exception>
    /// <exception cref="StaleAnswerError">The sequence number is not higher than the one held for the question.</exception>
    public void RecordAnswer(Guid questionId, Guid selectedOptionId, DateTime nowUtc, long? sequence = null) =>
        RecordAnswer(questionId, [selectedOptionId], nowUtc, sequence);

    /// <summary>
    /// Saves the options a candidate chose for a question, replacing an earlier choice for the same question. The set is the answer:
    /// saving again with different options replaces them all. The caller has already checked that the question belongs to the exam,
    /// that every option belongs to the question, and that a single-answer question got exactly one.
    /// </summary>
    /// <param name="questionId">The question answered.</param>
    /// <param name="selectedOptionIds">The options chosen; repeats are ignored, and at least one is needed (to take an answer back, clear it).</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <param name="sequence">The client's sequence number for this write, or null from a client that sends none; see <see cref="AttemptAnswerSequence"/>.</param>
    /// <exception cref="InvalidAttemptError">No option was given, or the sequence number is outside the range a client may send.</exception>
    /// <exception cref="AttemptNotInProgressError">The attempt is already submitted.</exception>
    /// <exception cref="AttemptTimeExpiredError">The deadline has passed.</exception>
    /// <exception cref="StaleAnswerError">The sequence number is not higher than the one held for the question.</exception>
    public void RecordAnswer(Guid questionId, IReadOnlyCollection<Guid> selectedOptionIds, DateTime nowUtc, long? sequence = null)
    {
        EnsureOpen(nowUtc);

        var chosen = selectedOptionIds.Distinct().ToList();
        if (chosen.Count == 0)
            throw new InvalidAttemptError("An answer needs at least one option; to take an answer back, clear it.");

        // After every other check, so a write refused for another reason does not use up its sequence number.
        ClaimAnswerSequence(questionId, sequence);

        var existing = _answers.FirstOrDefault(a => a.QuestionId == questionId);
        if (existing is null)
            _answers.Add(new AttemptAnswer(Id, questionId, chosen, nowUtc));
        else
            existing.Change(chosen, nowUtc);
    }

    /// <summary>
    /// Saves the answer a candidate typed to a text question, replacing any earlier answer to the question, typed or chosen. The caller has
    /// already checked that the question belongs to the exam and that it is a text question.
    /// </summary>
    /// <param name="questionId">The question answered.</param>
    /// <param name="text">What the candidate typed; surrounding spaces are removed. It must not be blank and must fit <see cref="TypedAnswer.MaxLength"/>.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <param name="sequence">The client's sequence number for this write, or null from a client that sends none; see <see cref="AttemptAnswerSequence"/>.</param>
    /// <exception cref="InvalidAttemptError">The answer is blank or longer than <see cref="TypedAnswer.MaxLength"/>, or the sequence number is outside the range a client may send.</exception>
    /// <exception cref="AttemptNotInProgressError">The attempt is already submitted.</exception>
    /// <exception cref="AttemptTimeExpiredError">The deadline has passed.</exception>
    /// <exception cref="StaleAnswerError">The sequence number is not higher than the one held for the question.</exception>
    public void RecordTextAnswer(Guid questionId, string? text, DateTime nowUtc, long? sequence = null)
    {
        EnsureOpen(nowUtc);

        var typed = text?.Trim() ?? string.Empty;
        if (typed.Length == 0)
            throw new InvalidAttemptError("A typed answer needs some text; to take an answer back, clear it.");
        if (typed.Length > TypedAnswer.MaxLength)
            throw new InvalidAttemptError($"A typed answer must be at most {TypedAnswer.MaxLength} characters.");

        ClaimAnswerSequence(questionId, sequence);

        var existing = _answers.FirstOrDefault(a => a.QuestionId == questionId);
        if (existing is null)
            _answers.Add(new AttemptAnswer(Id, questionId, [], nowUtc, typed));
        else
            existing.Change([], nowUtc, typed);
    }

    /// <summary>
    /// Takes back the option a candidate chose for a question, so it counts as unanswered again ("clear response", FR-18).
    /// Clearing a question with no answer does nothing: the candidate asked for "no answer" and that is what they have.
    /// </summary>
    /// <remarks>
    /// The answer is removed rather than kept with an empty choice, so everything that reads answers keeps meaning
    /// "a candidate chose this option": a cleared question is neither scored nor treated as answered.
    /// </remarks>
    /// <remarks>
    /// A clear takes a sequence number like a save, and keeps it even when there was nothing to clear, so a save sent before the clear that
    /// arrives after it cannot bring the answer back.
    /// </remarks>
    /// <param name="questionId">The question to clear.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <param name="sequence">The client's sequence number for this write, or null from a client that sends none; see <see cref="AttemptAnswerSequence"/>.</param>
    /// <exception cref="AttemptNotInProgressError">The attempt is already submitted.</exception>
    /// <exception cref="AttemptTimeExpiredError">The deadline has passed.</exception>
    /// <exception cref="InvalidAttemptError">The sequence number is outside the range a client may send.</exception>
    /// <exception cref="StaleAnswerError">The sequence number is not higher than the one held for the question.</exception>
    public void ClearAnswer(Guid questionId, DateTime nowUtc, long? sequence = null)
    {
        EnsureOpen(nowUtc);

        ClaimAnswerSequence(questionId, sequence);

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
    /// Records how many seconds the candidate has spent on a question so far, as the page measured it (FR-36). The report is a running total,
    /// not the time since the last report, so a report that is repeated or arrives late cannot count the same seconds twice.
    /// </summary>
    /// <remarks>
    /// The total is kept no higher than the time since the attempt began, because nobody can have read a question for longer than they have been
    /// sitting; a negative report counts as none. A total lower than the one kept is ignored, so a page that has lost its own count (after a
    /// reload, say) can never lower the record. The report is never refused for its size: it is a measure, and a candidate's answer must not be
    /// lost over it.
    /// </remarks>
    /// <param name="questionId">The question the time was spent on.</param>
    /// <param name="seconds">The running total for the question, in seconds, as the page has it.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <param name="keep">
    /// False when the time is withheld from this attempt (FR-36, section 7: a candidate under 18 while the switch is off). The report is
    /// checked exactly as any other, so a closed attempt is refused the same way, and then nothing is kept.
    /// </param>
    /// <exception cref="AttemptNotInProgressError">The attempt is already submitted.</exception>
    /// <exception cref="AttemptPausedError">An administrator has paused the attempt.</exception>
    /// <exception cref="AttemptTimeExpiredError">The deadline has passed.</exception>
    public void RecordQuestionTime(Guid questionId, int seconds, DateTime nowUtc, bool keep = true)
    {
        EnsureOpen(nowUtc);
        if (!keep)
            return;

        var sitting = (int)Math.Clamp(Math.Floor((nowUtc - StartedAtUtc).TotalSeconds), 0, int.MaxValue);
        var total = Math.Clamp(seconds, 0, sitting);
        var existing = _questionTimes.FirstOrDefault(t => t.QuestionId == questionId);

        if (existing is not null)
        {
            if (total > existing.Seconds)
                existing.RaiseTo(total);
        }
        else if (total > 0)
        {
            _questionTimes.Add(new AttemptQuestionTime(Id, questionId, total));
        }
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
    /// Notes where the candidate is sitting the attempt from (FR-26). The first call records the start. Later ones record something only
    /// when the address or device signature differs from the last one recorded, so the rows are a history of changes, and a candidate
    /// who stays put adds nothing. Does nothing once the attempt is over: a result opened later from another place is not a change.
    /// </summary>
    /// <param name="ipAddress">The candidate's address as the server sees it; null if unknown.</param>
    /// <param name="deviceFingerprint">The device signature the page sent; null if none.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <returns>Whether anything was recorded, so the caller knows there is something to save.</returns>
    public bool NoteClient(string? ipAddress, string? deviceFingerprint, DateTime nowUtc)
    {
        if (Status != AttemptStatus.InProgress)
            return false;

        var last = _clientSightings.OrderBy(s => s.SeenAtUtc).LastOrDefault();
        if (last is null)
        {
            _clientSightings.Add(new AttemptClientSighting(Id, ipAddress, deviceFingerprint, nowUtc, ClientSightingReason.Started));
            return true;
        }

        if (last.IpAddress == ipAddress && last.DeviceFingerprint == deviceFingerprint)
            return false;

        _clientSightings.Add(new AttemptClientSighting(Id, ipAddress, deviceFingerprint, nowUtc, ClientSightingReason.Changed));
        AddDomainEvent(new AttemptClientChangedEvent(Id, ExamId, CandidateId, last.IpAddress, last.DeviceFingerprint, ipAddress, deviceFingerprint));
        return true;
    }

    /// <summary>Sends the candidate a warning while they sit the exam (FR-29). A paused attempt can be warned too.</summary>
    /// <param name="message">What to tell them, 1 to <see cref="AttemptWarning.MaxMessageLength"/> characters.</param>
    /// <param name="byUserId">The administrator, from their token.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <exception cref="InvalidAttemptError">The message is empty or too long.</exception>
    /// <exception cref="AttemptNotInProgressError">The attempt is already over.</exception>
    public void Warn(string? message, Guid byUserId, DateTime nowUtc)
    {
        EnsureInProgress();
        var text = message?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > AttemptWarning.MaxMessageLength)
            throw new InvalidAttemptError($"A warning is from 1 to {AttemptWarning.MaxMessageLength} characters.");

        _warnings.Add(new AttemptWarning(Id, text, byUserId, nowUtc));
        AddDomainEvent(new AttemptWarnedEvent(Id, ExamId, CandidateId, text));
    }

    /// <summary>Pauses the attempt (FR-29): the candidate cannot answer, and the deadline stops moving towards them.</summary>
    /// <param name="nowUtc">The current instant.</param>
    /// <exception cref="AttemptNotInProgressError">The attempt is already over.</exception>
    /// <exception cref="AttemptAlreadyPausedError">The attempt is already paused.</exception>
    /// <exception cref="AttemptTimeExpiredError">The deadline has passed.</exception>
    public void Pause(DateTime nowUtc)
    {
        EnsureInProgress();
        if (PausedAtUtc is not null)
            throw new AttemptAlreadyPausedError();
        if (IsExpired(nowUtc))
            throw new AttemptTimeExpiredError();

        PausedAtUtc = nowUtc;
        AddDomainEvent(new AttemptPausedEvent(Id, ExamId, CandidateId));
    }

    /// <summary>
    /// Resumes a paused attempt, and moves its deadline later by the time it was paused so the candidate gets back exactly the time
    /// they had (FR-29). The server's deadline is the only clock, so nothing on the candidate's side needs to change.
    /// </summary>
    /// <param name="nowUtc">The current instant.</param>
    /// <exception cref="AttemptNotInProgressError">The attempt is already over.</exception>
    /// <exception cref="AttemptNotPausedError">The attempt is not paused.</exception>
    public void Resume(DateTime nowUtc)
    {
        EnsureInProgress();
        if (PausedAtUtc is null)
            throw new AttemptNotPausedError();

        var paused = nowUtc - PausedAtUtc.Value;
        DeadlineUtc += paused;
        PausedAtUtc = null;
        AddDomainEvent(new AttemptResumedEvent(Id, ExamId, CandidateId, (int)paused.TotalSeconds));
    }

    /// <summary>
    /// Ends the attempt early on an administrator's decision, scored with the answers saved so far (FR-29). Unlike a submit it works on
    /// a paused attempt. The reason is kept and shown to the candidate.
    /// </summary>
    /// <param name="nowUtc">The current instant.</param>
    /// <param name="score">The marks scored; computed by the caller from the answer key.</param>
    /// <param name="maxScore">The marks available.</param>
    /// <param name="byUserId">The administrator, from their token.</param>
    /// <param name="reason">Why, 1 to <see cref="MaxReasonLength"/> characters.</param>
    /// <exception cref="InvalidAttemptError">The reason is empty or too long.</exception>
    /// <exception cref="AttemptNotInProgressError">The attempt is already submitted.</exception>
    public void Terminate(DateTime nowUtc, decimal score, decimal maxScore, Guid byUserId, string? reason)
    {
        var text = CleanReason(reason);
        if (Status != AttemptStatus.InProgress)
            throw new AttemptNotInProgressError();

        PausedAtUtc = null;
        TerminatedByUserId = byUserId;
        TerminationReason = text;
        Close(nowUtc, score, maxScore, endedByViolations: false, endedByAdmin: true);
        AddDomainEvent(new AttemptTerminatedEvent(Id, ExamId, CandidateId, text));
    }

    /// <summary>
    /// Ends the attempt early on an administrator's decision like <see cref="Terminate"/>, but holds back the score: the typed answers named
    /// still have to be checked by the AI evaluator, and <see cref="ApplyEvaluatedScore"/> gives the score when they have been.
    /// </summary>
    /// <param name="nowUtc">The current instant.</param>
    /// <param name="byUserId">The administrator, from their token.</param>
    /// <param name="reason">Why, 1 to <see cref="MaxReasonLength"/> characters.</param>
    /// <param name="questionsToEvaluate">The questions whose typed answers are to be checked; at least one.</param>
    /// <exception cref="InvalidAttemptError">The reason is empty or too long, or a question named has no typed answer, or none is named.</exception>
    /// <exception cref="AttemptNotInProgressError">The attempt is already submitted.</exception>
    public void TerminateForGrading(DateTime nowUtc, Guid byUserId, string? reason, IReadOnlyCollection<Guid> questionsToEvaluate)
    {
        var text = CleanReason(reason);
        if (Status != AttemptStatus.InProgress)
            throw new AttemptNotInProgressError();

        QueueForEvaluation(questionsToEvaluate, nowUtc);
        PausedAtUtc = null;
        TerminatedByUserId = byUserId;
        TerminationReason = text;
        Close(nowUtc, score: null, maxScore: null, endedByViolations: false, endedByAdmin: true);
        AddDomainEvent(new AttemptTerminatedEvent(Id, ExamId, CandidateId, text));
    }

    /// <summary>
    /// Invalidates a submitted attempt's result (FR-29), for example after proof of cheating: it no longer counts, and the candidate is
    /// told why instead of being shown a score. The score is kept for the record.
    /// </summary>
    /// <param name="byUserId">The administrator, from their token.</param>
    /// <param name="reason">Why, 1 to <see cref="MaxReasonLength"/> characters.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <exception cref="InvalidAttemptError">The reason is empty or too long.</exception>
    /// <exception cref="AttemptNotSubmittedError">The attempt is still open: end it first.</exception>
    /// <exception cref="AttemptAlreadyInvalidatedError">The result was already invalidated.</exception>
    public void Invalidate(Guid byUserId, string? reason, DateTime nowUtc)
    {
        var text = CleanReason(reason);
        if (Status == AttemptStatus.Grading)
            throw new AttemptBeingGradedError();
        if (Status != AttemptStatus.Submitted)
            throw new AttemptNotSubmittedError();
        if (IsInvalidated)
            throw new AttemptAlreadyInvalidatedError();

        InvalidatedAtUtc = nowUtc;
        InvalidatedByUserId = byUserId;
        InvalidationReason = text;
        AddDomainEvent(new AttemptInvalidatedEvent(Id, ExamId, CandidateId, text));
    }

    /// <summary>The longest reason an administrator can give for ending or invalidating an attempt.</summary>
    public const int MaxReasonLength = 500;

    private static string CleanReason(string? reason)
    {
        var text = reason?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > MaxReasonLength)
            throw new InvalidAttemptError($"Give a reason of 1 to {MaxReasonLength} characters; the candidate is shown it.");

        return text;
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

        // A paused attempt is the administrator's to end or resume; the candidate cannot slip out of it by submitting.
        if (PausedAtUtc is not null)
            throw new AttemptPausedError();

        Close(nowUtc, score, maxScore, endedByViolations, endedByAdmin: false);
    }

    /// <summary>
    /// Ends the attempt like <see cref="Submit"/>, but holds back the score: the typed answers named still have to be checked by the AI
    /// evaluator, which can take a while and can fail and be tried again. The attempt is <see cref="AttemptStatus.Grading"/> until
    /// <see cref="ApplyEvaluatedScore"/> gives it its score, and nothing that reads results counts it before then.
    /// </summary>
    /// <param name="nowUtc">The current instant.</param>
    /// <param name="questionsToEvaluate">The questions whose typed answers are to be checked; at least one.</param>
    /// <param name="endedByViolations">Whether the server is ending the attempt because the candidate left the exam page too often (FR-22).</param>
    /// <exception cref="InvalidAttemptError">None is named, or a question named has no typed answer.</exception>
    /// <exception cref="AttemptNotInProgressError">The attempt is already submitted.</exception>
    /// <exception cref="AttemptPausedError">An administrator paused the attempt.</exception>
    public void SubmitForGrading(DateTime nowUtc, IReadOnlyCollection<Guid> questionsToEvaluate, bool endedByViolations = false)
    {
        if (Status != AttemptStatus.InProgress)
            throw new AttemptNotInProgressError();

        if (PausedAtUtc is not null)
            throw new AttemptPausedError();

        QueueForEvaluation(questionsToEvaluate, nowUtc);
        Close(nowUtc, score: null, maxScore: null, endedByViolations, endedByAdmin: false);
    }

    private void Close(DateTime nowUtc, decimal? score, decimal? maxScore, bool endedByViolations, bool endedByAdmin)
    {
        // Ended by the server either way: time ran out, or the violation limit was reached.
        AutoSubmitted = IsExpired(nowUtc) || endedByViolations || endedByAdmin;
        EndedByViolations = endedByViolations;
        // Never later than the deadline: an attempt picked up and closed hours afterwards still records
        // that the candidate stopped when time ran out.
        SubmittedAtUtc = IsExpired(nowUtc) ? DeadlineUtc : nowUtc;
        Score = score;
        MaxScore = maxScore;
        // No score yet means the typed answers are still being checked.
        Status = score is null ? AttemptStatus.Grading : AttemptStatus.Submitted;
    }

    // Queues the typed answers to the named questions for the evaluator. Every one must be a typed answer this attempt holds: asking about
    // a question the candidate left blank, or answered by choosing options, would be a mistake in the caller, and failing loudly beats
    // an attempt that waits forever for a verdict nobody will give.
    private void QueueForEvaluation(IReadOnlyCollection<Guid> questionIds, DateTime nowUtc)
    {
        if (questionIds.Count == 0)
            throw new InvalidAttemptError("There is no typed answer to check.");

        foreach (var questionId in questionIds.Distinct())
        {
            var answer = _answers.FirstOrDefault(a => a.QuestionId == questionId && !string.IsNullOrWhiteSpace(a.AnswerText))
                ?? throw new InvalidAttemptError("A typed answer can only be checked for a question the candidate typed an answer to.");
            answer.RequestEvaluation(nowUtc);
        }
    }

    /// <summary>
    /// Queues the typed answers to the named questions to be checked again, because staff corrected a question's answer key and the earlier
    /// verdicts were given against the old one (FR-31). The attempt keeps its score, and each answer keeps its verdict, until the new
    /// verdicts are in; then <see cref="ApplyEvaluatedScore"/> revises the score with <paramref name="reason"/>.
    /// </summary>
    /// <param name="questionIds">The questions whose typed answers are to be checked again; at least one.</param>
    /// <param name="reason">Why the score may change, shown to the candidate if it does.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <exception cref="AttemptNotSubmittedError">The attempt has no score yet to revise.</exception>
    /// <exception cref="InvalidAttemptError">None is named, or a question named has no typed answer.</exception>
    public void RequestReevaluation(IReadOnlyCollection<Guid> questionIds, string reason, DateTime nowUtc)
    {
        if (Status != AttemptStatus.Submitted)
            throw new AttemptNotSubmittedError();

        QueueForEvaluation(questionIds, nowUtc);
        var text = reason.Trim();
        PendingRevisionReason = text.Length <= MaxRevisionReasonLength ? text : text[..MaxRevisionReasonLength];
    }

    /// <summary>
    /// Queues one typed answer to be checked again while the attempt is still <see cref="AttemptStatus.Grading"/>, because its question's
    /// answer key was corrected before the first verdict. The score is still to be given, so there is nothing to revise.
    /// </summary>
    /// <param name="questionId">The question.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <exception cref="AttemptNotGradingError">The attempt is not waiting for its typed answers to be checked.</exception>
    public void RestartEvaluation(Guid questionId, DateTime nowUtc)
    {
        if (Status != AttemptStatus.Grading)
            throw new AttemptNotGradingError();

        QueueForEvaluation([questionId], nowUtc);
    }

    /// <summary>Records the evaluator's verdict on the typed answer to a question, and takes that answer out of the queue.</summary>
    /// <param name="questionId">The question.</param>
    /// <param name="verdict">Right or wrong.</param>
    /// <param name="reason">The evaluator's reason, for staff.</param>
    /// <param name="model">The model that decided.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <exception cref="InvalidAttemptError">That answer is not waiting for a verdict.</exception>
    public void RecordEvaluation(Guid questionId, TypedAnswerVerdict verdict, string? reason, string? model, DateTime nowUtc) =>
        AnswerAwaitingEvaluation(questionId).RecordEvaluation(verdict, reason, model, nowUtc);

    /// <summary>Records that checking the typed answer to a question failed, and when to try again. It stays in the queue.</summary>
    /// <param name="questionId">The question.</param>
    /// <param name="error">What went wrong, in a few words, without the answer's content.</param>
    /// <param name="nextAttemptAtUtc">The earliest instant to try again.</param>
    /// <exception cref="InvalidAttemptError">That answer is not waiting for a verdict.</exception>
    public void RecordEvaluationFailure(Guid questionId, string error, DateTime nextAttemptAtUtc) =>
        AnswerAwaitingEvaluation(questionId).RecordEvaluationFailure(error, nextAttemptAtUtc);

    /// <summary>
    /// Lets go of the typed answer to a question that a worker was holding, without counting a failure, so another worker can take it at once.
    /// Does nothing when the answer is not waiting for a verdict.
    /// </summary>
    /// <param name="questionId">The question.</param>
    public void ReleaseEvaluation(Guid questionId) =>
        _answers.FirstOrDefault(a => a.QuestionId == questionId && a.IsAwaitingEvaluation)?.ReleaseEvaluation();

    private AttemptAnswer AnswerAwaitingEvaluation(Guid questionId) =>
        _answers.FirstOrDefault(a => a.QuestionId == questionId && a.IsAwaitingEvaluation)
        ?? throw new InvalidAttemptError("That answer is not waiting for a verdict.");

    /// <summary>
    /// Gives the attempt the score worked out once every typed answer has its verdict. An attempt that was <see cref="AttemptStatus.Grading"/>
    /// becomes <see cref="AttemptStatus.Submitted"/> with this as its score. An attempt that was submitted and queued by
    /// <see cref="RequestReevaluation"/> has its score revised, with the reason given then.
    /// </summary>
    /// <param name="score">The marks scored, worked out with the verdicts.</param>
    /// <param name="maxScore">The marks available.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <returns>Whether the attempt's score is now different from before; the first score of a graded attempt always is.</returns>
    /// <exception cref="InvalidAttemptError">A typed answer is still waiting for its verdict.</exception>
    /// <exception cref="AttemptNotGradingError">The attempt is not waiting for its typed answers to be checked.</exception>
    public bool ApplyEvaluatedScore(decimal score, decimal maxScore, DateTime nowUtc)
    {
        if (IsAwaitingEvaluation)
            throw new InvalidAttemptError("A typed answer is still waiting for its verdict.");

        if (Status == AttemptStatus.Grading)
        {
            Score = score;
            MaxScore = maxScore;
            PendingRevisionReason = null;
            Status = AttemptStatus.Submitted;
            return true;
        }

        if (Status == AttemptStatus.Submitted && PendingRevisionReason is { } reason)
        {
            PendingRevisionReason = null;
            return ReviseScore(score, maxScore, reason, nowUtc);
        }

        throw new AttemptNotGradingError();
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

    private void EnsureInProgress()
    {
        if (Status != AttemptStatus.InProgress)
            throw new AttemptNotInProgressError();
    }

    /// <summary>The one rule every change to an open attempt shares: it must still be open, not paused, and its time must not have run out.</summary>
    // Takes the sequence number for a write to a question's answer, or refuses the write as out of date (NFR-4, ADR 0004). A write that
    // names its number must be higher than the one held. A write that names none, from a client older than sequence numbers, is the newest
    // by arrival, as every write was before them, so it takes the next number up and the order stays what it was.
    private void ClaimAnswerSequence(Guid questionId, long? sequence)
    {
        if (sequence is < 1 or > AttemptAnswerSequence.MaxValue)
            throw new InvalidAttemptError($"An answer's sequence number must be between 1 and {AttemptAnswerSequence.MaxValue}.");

        var held = _answerSequences.FirstOrDefault(s => s.QuestionId == questionId);
        var heldValue = held?.Value ?? 0;
        if (sequence <= heldValue)
            throw new StaleAnswerError(heldValue);

        var claimed = sequence ?? Math.Min(heldValue + 1, AttemptAnswerSequence.MaxValue);
        if (held is null)
            _answerSequences.Add(new AttemptAnswerSequence(Id, questionId, claimed));
        else if (claimed > heldValue)
            held.RaiseTo(claimed);
    }

    private void EnsureOpen(DateTime nowUtc)
    {
        if (Status != AttemptStatus.InProgress)
            throw new AttemptNotInProgressError();
        if (PausedAtUtc is not null)
            throw new AttemptPausedError();
        if (IsExpired(nowUtc))
            throw new AttemptTimeExpiredError();
    }
}
