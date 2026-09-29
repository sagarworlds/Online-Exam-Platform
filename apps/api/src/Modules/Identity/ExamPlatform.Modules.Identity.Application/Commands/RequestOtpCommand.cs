using ExamPlatform.Modules.Identity.Domain;

namespace ExamPlatform.Modules.Identity.Application.Commands;

/// <summary>
/// Requests a login OTP be sent to an existing candidate (FR-1). Registration
/// and staff 2FA issue their own challenges internally (see
/// <see cref="RegisterCandidateHandler"/> and <see cref="PasswordLoginHandler"/>) —
/// this command only covers the "log in to an existing account" case.
/// </summary>
/// <param name="Channel">How to deliver the code.</param>
/// <param name="Destination">The email address or phone number to deliver to.</param>
public sealed record RequestOtpCommand(OtpChannel Channel, string Destination);
