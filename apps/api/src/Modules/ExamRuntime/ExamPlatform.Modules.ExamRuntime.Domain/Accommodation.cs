using ExamPlatform.Modules.ExamRuntime.Domain.Events;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain;

/// <summary>
/// What an administrator allows one candidate at one exam because of a disability or another need (FR-49): extra time, a reader or scribe,
/// and alternate formats of the exam page. One per candidate per exam; changing it replaces it.
/// </summary>
/// <remarks>
/// It applies to attempts that start after it is set, and, for time and formats, to an attempt already in progress (see
/// <see cref="Attempt.ApplyAccommodation"/>). The attempt keeps what applied to it, so a later change never rewrites an old sitting.
/// An aggregate so setting and removing one raises events and is audited (FR-40).
/// </remarks>
public sealed class Accommodation : AggregateRoot
{
    /// <summary>The most extra time that may be given, 12 hours: far beyond any real need, so a typo cannot hand out days.</summary>
    public const int MaxExtraTimeSeconds = 12 * 60 * 60;

    /// <summary>The longest note.</summary>
    public const int MaxNotesLength = 500;

    /// <summary>The exam.</summary>
    public Guid ExamId { get; private set; }

    /// <summary>The candidate; the signed-in user's id.</summary>
    public Guid CandidateId { get; private set; }

    /// <summary>Time added to the candidate's deadline, in seconds; 0 for none. Added on top of whatever other candidates have.</summary>
    public int ExtraTimeSeconds { get; private set; }

    /// <summary>Whether the candidate may use a reader or scribe. Staff are told; the exam page does nothing differently.</summary>
    public bool ReaderScribe { get; private set; }

    /// <summary>The alternate formats of the exam page the candidate gets, from <see cref="AccommodationFormat.All"/>, each once.</summary>
    public string[] AlternateFormats { get; private set; } = [];

    /// <summary>
    /// A note for staff, such as which certificate was seen. Personal and sensitive, so it is shown to staff only: never to the candidate
    /// and never in the audit trail.
    /// </summary>
    public string? Notes { get; private set; }

    /// <summary>The staff user who last set it.</summary>
    public Guid UpdatedByUserId { get; private set; }

    /// <summary>When it was last set.</summary>
    public DateTime UpdatedAtUtc { get; private set; }

    // For EF Core.
    private Accommodation() : base(Guid.Empty)
    {
    }

    private Accommodation(Guid examId, Guid candidateId) : base(Guid.NewGuid())
    {
        ExamId = examId;
        CandidateId = candidateId;
    }

    /// <summary>Gives a candidate an accommodation.</summary>
    /// <param name="examId">The exam.</param>
    /// <param name="candidateId">The candidate.</param>
    /// <param name="extraTimeSeconds">Extra time in seconds, 0 to <see cref="MaxExtraTimeSeconds"/>.</param>
    /// <param name="readerScribe">Whether a reader or scribe is allowed.</param>
    /// <param name="alternateFormats">The formats, by code; see <see cref="AccommodationFormat"/>. Null means none.</param>
    /// <param name="notes">A note for staff; blank is stored as none.</param>
    /// <param name="byUserId">The staff user, from their token.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <exception cref="InvalidAccommodationError">The accommodation gives nothing, the time is out of range, a format is unknown, or the note is too long.</exception>
    public static Accommodation Create(
        Guid examId, Guid candidateId, int extraTimeSeconds, bool readerScribe, IEnumerable<string?>? alternateFormats, string? notes, Guid byUserId, DateTime nowUtc)
    {
        var accommodation = new Accommodation(examId, candidateId);
        accommodation.Set(extraTimeSeconds, readerScribe, alternateFormats, notes, byUserId, nowUtc);
        return accommodation;
    }

    /// <summary>Replaces what is given; the arguments are the whole new accommodation, not a patch.</summary>
    /// <param name="extraTimeSeconds">Extra time in seconds, 0 to <see cref="MaxExtraTimeSeconds"/>.</param>
    /// <param name="readerScribe">Whether a reader or scribe is allowed.</param>
    /// <param name="alternateFormats">The formats, by code. Null means none.</param>
    /// <param name="notes">A note for staff; blank is stored as none.</param>
    /// <param name="byUserId">The staff user, from their token.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <exception cref="InvalidAccommodationError">As for <see cref="Create"/>; nothing changes when it is thrown.</exception>
    public void Revise(int extraTimeSeconds, bool readerScribe, IEnumerable<string?>? alternateFormats, string? notes, Guid byUserId, DateTime nowUtc) =>
        Set(extraTimeSeconds, readerScribe, alternateFormats, notes, byUserId, nowUtc);

    private void Set(int extraTimeSeconds, bool readerScribe, IEnumerable<string?>? alternateFormats, string? notes, Guid byUserId, DateTime nowUtc)
    {
        if (extraTimeSeconds is < 0 or > MaxExtraTimeSeconds)
            throw new InvalidAccommodationError($"Extra time must be between 0 and {MaxExtraTimeSeconds / 60} minutes.");

        var formats = (alternateFormats ?? []).Select(f => f?.Trim().ToLowerInvariant() ?? string.Empty).Distinct().ToList();
        var unknown = formats.FirstOrDefault(f => !AccommodationFormat.All.Contains(f));
        if (unknown is not null)
            throw new InvalidAccommodationError($"'{unknown}' is not a format that can be given. Choose from: {string.Join(", ", AccommodationFormat.All)}.");

        var note = notes?.Trim();
        if (note is { Length: > MaxNotesLength })
            throw new InvalidAccommodationError($"The note must be at most {MaxNotesLength} characters.");

        // An accommodation that gives nothing is a row that says nothing; taking one away is its own action, so it is never saved by accident.
        if (extraTimeSeconds == 0 && !readerScribe && formats.Count == 0)
            throw new InvalidAccommodationError("An accommodation must give something: extra time, a reader or scribe, or an alternate format. To take one away, remove it.");

        ExtraTimeSeconds = extraTimeSeconds;
        ReaderScribe = readerScribe;
        AlternateFormats = formats.ToArray();
        Notes = string.IsNullOrEmpty(note) ? null : note;
        UpdatedByUserId = byUserId;
        UpdatedAtUtc = nowUtc;
        AddDomainEvent(new AccommodationSetEvent(Id, ExamId, CandidateId, ExtraTimeSeconds, ReaderScribe, AlternateFormats));
    }
}
