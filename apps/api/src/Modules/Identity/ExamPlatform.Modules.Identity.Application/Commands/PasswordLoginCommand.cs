namespace ExamPlatform.Modules.Identity.Application.Commands;

/// <summary>
/// Password-based login for staff/admin roles (FR-3). Candidates never use this —
/// they log in via OTP only (see <see cref="RequestOtpCommand"/>).
/// </summary>
/// <param name="Email">The account's email address.</param>
/// <param name="Password">The plaintext password.</param>
/// <param name="DeviceFingerprint">Client device fingerprint, if captured.</param>
/// <param name="IpAddress">Client IP address, if captured.</param>
public sealed record PasswordLoginCommand(string Email, string Password, string? DeviceFingerprint, string? IpAddress);
