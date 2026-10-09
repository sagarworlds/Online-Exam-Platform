using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain.Events;

/// <summary>An administrator set or changed a candidate's accommodation (FR-49). The staff note is deliberately not carried: it is sensitive and stays out of the audit trail.</summary>
/// <param name="AccommodationId">The accommodation.</param>
/// <param name="ExamId">The exam.</param>
/// <param name="CandidateId">The candidate.</param>
/// <param name="ExtraTimeSeconds">The extra time now given.</param>
/// <param name="ReaderScribe">Whether a reader or scribe is allowed.</param>
/// <param name="AlternateFormats">The formats now given.</param>
public sealed record AccommodationSetEvent(
    Guid AccommodationId, Guid ExamId, Guid CandidateId, int ExtraTimeSeconds, bool ReaderScribe, IReadOnlyList<string> AlternateFormats) : DomainEvent;

/// <summary>An attempt took on an accommodation: as it started, or while it was in progress (FR-49).</summary>
/// <param name="AttemptId">The attempt.</param>
/// <param name="ExamId">The exam.</param>
/// <param name="CandidateId">The candidate.</param>
/// <param name="ExtraTimeSeconds">The extra time the attempt now carries in all.</param>
/// <param name="AddedSeconds">How much later the deadline became because of this; 0 when only formats or the reader flag changed.</param>
public sealed record AttemptAccommodatedEvent(Guid AttemptId, Guid ExamId, Guid CandidateId, int ExtraTimeSeconds, int AddedSeconds) : DomainEvent;
