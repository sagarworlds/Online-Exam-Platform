using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain.Events;

/// <summary>An open attempt was seen from a different address or device than before (FR-26).</summary>
/// <param name="AttemptId">The attempt.</param>
/// <param name="ExamId">The exam.</param>
/// <param name="CandidateId">The candidate.</param>
/// <param name="PreviousIpAddress">Where it was last seen from.</param>
/// <param name="PreviousDeviceFingerprint">The device it was last seen on.</param>
/// <param name="IpAddress">Where it is seen from now.</param>
/// <param name="DeviceFingerprint">The device it is seen on now.</param>
public sealed record AttemptClientChangedEvent(
    Guid AttemptId,
    Guid ExamId,
    Guid CandidateId,
    string? PreviousIpAddress,
    string? PreviousDeviceFingerprint,
    string? IpAddress,
    string? DeviceFingerprint) : DomainEvent;
