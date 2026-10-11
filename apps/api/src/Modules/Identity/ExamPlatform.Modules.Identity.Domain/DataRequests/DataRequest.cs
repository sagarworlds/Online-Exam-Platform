using ExamPlatform.Modules.Identity.Domain.Exceptions;
using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Identity.Domain.DataRequests;

/// <summary>
/// A candidate's request under the data-principal rights (FR-48): to see their data, to correct it, or to have it erased. Staff answer it
/// within the service period, and the answer is kept with who gave it and when.
/// </summary>
/// <remarks>
/// The request records what was asked and what was decided. Carrying the decision out (sending the copy, changing the record, erasing the
/// data) is staff work outside this aggregate, so a completed request is a statement by staff, not something the platform did on its own.
/// </remarks>
public sealed class DataRequest : AggregateRoot
{
    /// <summary>The longest details a candidate may give, after surrounding whitespace is removed.</summary>
    public const int MaxDetailsLength = 1000;

    /// <summary>The longest note staff may give, after surrounding whitespace is removed.</summary>
    public const int MaxNoteLength = 1000;

    private DataRequest(Guid id, Guid userId, DataRequestKind kind, string? details, DateTime receivedAtUtc, DateTime dueAtUtc)
        : base(id)
    {
        UserId = userId;
        Kind = kind;
        Details = details;
        Status = DataRequestStatus.Received;
        ReceivedAtUtc = receivedAtUtc;
        DueAtUtc = dueAtUtc;
    }

    /// <summary>The account the request is about and was made from.</summary>
    public Guid UserId { get; private set; }

    /// <summary>What is asked for.</summary>
    public DataRequestKind Kind { get; private set; }

    /// <summary>What the candidate said, if anything. Free text, so it is never trusted as markup.</summary>
    public string? Details { get; private set; }

    /// <summary>Where the request stands.</summary>
    public DataRequestStatus Status { get; private set; }

    /// <summary>When the request was received.</summary>
    public DateTime ReceivedAtUtc { get; private set; }

    /// <summary>When staff must have answered it by (FR-48's SLA).</summary>
    public DateTime DueAtUtc { get; private set; }

    /// <summary>When staff answered it, if they have.</summary>
    public DateTime? ResolvedAtUtc { get; private set; }

    /// <summary>The staff user who answered it, if one has.</summary>
    public Guid? ResolvedByUserId { get; private set; }

    /// <summary>Why it was answered as it was. Required for a refusal.</summary>
    public string? ResolutionNote { get; private set; }

    /// <summary>Whether staff can still answer it.</summary>
    public bool IsOpen => Status == DataRequestStatus.Received;

    /// <summary>Whether it is still open after its due time, so it is late for the SLA.</summary>
    /// <param name="nowUtc">The current instant.</param>
    public bool IsOverdue(DateTime nowUtc) => IsOpen && nowUtc > DueAtUtc;

    /// <summary>Receives a new request. The details are optional and trimmed; blank means none.</summary>
    /// <param name="userId">The account making the request.</param>
    /// <param name="kind">What is asked for.</param>
    /// <param name="details">What the candidate said, or null.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <param name="servicePeriod">How long staff have to answer it.</param>
    /// <exception cref="InvalidDataRequestError">The details are longer than <see cref="MaxDetailsLength"/>.</exception>
    public static DataRequest Raise(Guid userId, DataRequestKind kind, string? details, DateTime nowUtc, TimeSpan servicePeriod)
    {
        var trimmed = string.IsNullOrWhiteSpace(details) ? null : details.Trim();
        if (trimmed is not null && trimmed.Length > MaxDetailsLength)
            throw InvalidDataRequestError.DetailsTooLong(MaxDetailsLength);

        return new DataRequest(Guid.NewGuid(), userId, kind, trimmed, nowUtc, nowUtc.Add(servicePeriod));
    }

    /// <summary>
    /// Records staff's answer. A request can be answered once; a refusal must say why, so the candidate is never refused without a reason.
    /// </summary>
    /// <param name="resolvedByUserId">The staff user answering it.</param>
    /// <param name="outcome">Completed or Rejected.</param>
    /// <param name="note">What was done or why it was refused.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <exception cref="DataRequestNotOpenError">It was already answered.</exception>
    /// <exception cref="InvalidDataRequestError">The outcome is not one of the two answers, or the note is missing for a refusal or too long.</exception>
    public void Resolve(Guid resolvedByUserId, DataRequestStatus outcome, string? note, DateTime nowUtc)
    {
        if (!IsOpen)
            throw new DataRequestNotOpenError();
        if (outcome is not (DataRequestStatus.Completed or DataRequestStatus.Rejected))
            throw InvalidDataRequestError.UnknownOutcome();

        var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (outcome == DataRequestStatus.Rejected && trimmed is null)
            throw InvalidDataRequestError.NoteRequiredForRefusal();
        if (trimmed is not null && trimmed.Length > MaxNoteLength)
            throw InvalidDataRequestError.NoteTooLong(MaxNoteLength);

        Status = outcome;
        ResolvedAtUtc = nowUtc;
        ResolvedByUserId = resolvedByUserId;
        ResolutionNote = trimmed;
    }
}
