using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain.Events;

/// <summary>An administrator sent a candidate a warning during their attempt (FR-29).</summary>
/// <param name="AttemptId">The attempt.</param>
/// <param name="ExamId">The exam.</param>
/// <param name="CandidateId">The candidate warned.</param>
/// <param name="Message">What the warning said.</param>
public sealed record AttemptWarnedEvent(Guid AttemptId, Guid ExamId, Guid CandidateId, string Message) : DomainEvent;

/// <summary>An administrator paused an attempt: the candidate cannot answer and the clock stops (FR-29).</summary>
/// <param name="AttemptId">The attempt.</param>
/// <param name="ExamId">The exam.</param>
/// <param name="CandidateId">The candidate.</param>
public sealed record AttemptPausedEvent(Guid AttemptId, Guid ExamId, Guid CandidateId) : DomainEvent;

/// <summary>An administrator resumed a paused attempt, and its deadline moved later by the time it was paused (FR-29).</summary>
/// <param name="AttemptId">The attempt.</param>
/// <param name="ExamId">The exam.</param>
/// <param name="CandidateId">The candidate.</param>
/// <param name="PausedSeconds">How long it was paused, which is how much later the deadline now is.</param>
public sealed record AttemptResumedEvent(Guid AttemptId, Guid ExamId, Guid CandidateId, int PausedSeconds) : DomainEvent;

/// <summary>An administrator ended an attempt early; it was scored with the answers saved so far (FR-29).</summary>
/// <param name="AttemptId">The attempt.</param>
/// <param name="ExamId">The exam.</param>
/// <param name="CandidateId">The candidate.</param>
/// <param name="Reason">Why, as the administrator wrote it; the candidate sees it too.</param>
public sealed record AttemptTerminatedEvent(Guid AttemptId, Guid ExamId, Guid CandidateId, string Reason) : DomainEvent;

/// <summary>An administrator invalidated an attempt's result, so it no longer counts (FR-29).</summary>
/// <param name="AttemptId">The attempt.</param>
/// <param name="ExamId">The exam.</param>
/// <param name="CandidateId">The candidate.</param>
/// <param name="Reason">Why, as the administrator wrote it; the candidate sees it too.</param>
public sealed record AttemptInvalidatedEvent(Guid AttemptId, Guid ExamId, Guid CandidateId, string Reason) : DomainEvent;
