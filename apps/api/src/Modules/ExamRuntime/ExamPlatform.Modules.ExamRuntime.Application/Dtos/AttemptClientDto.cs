using ExamPlatform.Modules.ExamRuntime.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Application.Dtos;

/// <summary>Where an attempt was being sat from at a moment, as staff see it (FR-26). Never shown to a candidate.</summary>
/// <param name="IpAddress">The candidate's IP address as the server saw it; null when it could not be read.</param>
/// <param name="DeviceFingerprint">The device signature the web app sent; null when none was sent. Compare it, do not read it: it is a hash.</param>
/// <param name="SeenAtUtc">When the server saw it.</param>
/// <param name="Reason">Whether this is where the attempt began or a change from where it was.</param>
public sealed record AttemptClientDto(string? IpAddress, string? DeviceFingerprint, DateTime SeenAtUtc, ClientSightingReason Reason);
