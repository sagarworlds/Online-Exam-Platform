using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Identity.Domain.Events;

/// <summary>Raised when a new login revokes a candidate's previously active session (FR-4).</summary>
/// <param name="UserId">The user whose session was superseded.</param>
/// <param name="SupersededSessionId">The session that was revoked.</param>
/// <param name="NewSessionId">The session that replaced it.</param>
/// <param name="OccurredAtUtc">When the supersession happened.</param>
/// <param name="SupersededIpAddress">Where the revoked session signed in from (FR-26), if it was captured.</param>
/// <param name="SupersededDeviceFingerprint">The device signature of the revoked session, if it was captured.</param>
/// <param name="NewIpAddress">Where the new session signed in from.</param>
/// <param name="NewDeviceFingerprint">The device signature of the new session.</param>
public sealed record SessionSupersededEvent(
    Guid UserId,
    Guid SupersededSessionId,
    Guid NewSessionId,
    DateTime OccurredAtUtc,
    string? SupersededIpAddress = null,
    string? SupersededDeviceFingerprint = null,
    string? NewIpAddress = null,
    string? NewDeviceFingerprint = null) : IDomainEvent;
