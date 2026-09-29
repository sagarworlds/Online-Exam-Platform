using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Identity.Domain.Events;

/// <summary>Raised when a new login revokes a candidate's previously active session (FR-4).</summary>
/// <param name="UserId">The user whose session was superseded.</param>
/// <param name="SupersededSessionId">The session that was revoked.</param>
/// <param name="NewSessionId">The session that replaced it.</param>
/// <param name="OccurredAtUtc">When the supersession happened.</param>
public sealed record SessionSupersededEvent(
    Guid UserId,
    Guid SupersededSessionId,
    Guid NewSessionId,
    DateTime OccurredAtUtc) : IDomainEvent;
