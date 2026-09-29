namespace ExamPlatform.Modules.Identity.Application.Commands;

/// <summary>
/// Completes an OTP challenge — the single endpoint used for candidate login
/// (FR-1), registration confirmation, and the staff 2FA step (FR-3), since all
/// three are "check a code, then start a session" with the same mechanics.
/// </summary>
/// <param name="OtpChallengeId">The challenge id returned when the OTP was requested.</param>
/// <param name="Code">The plaintext code the user supplied.</param>
/// <param name="DeviceFingerprint">Client device fingerprint, if captured.</param>
/// <param name="IpAddress">Client IP address, if captured.</param>
public sealed record VerifyOtpCommand(Guid OtpChallengeId, string Code, string? DeviceFingerprint, string? IpAddress);
