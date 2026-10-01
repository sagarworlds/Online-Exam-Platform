using ExamPlatform.Modules.Identity.Domain;

namespace ExamPlatform.Modules.Identity.Application.Commands;

/// <summary>
/// Requests a one-time code for signing in to an existing candidate account by email
/// or phone (FR-1). The response is the same whether or not an account exists, so
/// this cannot be used to discover accounts. Registration and staff 2FA issue their
/// own challenges internally (see <see cref="RegisterCandidateHandler"/> and
/// <see cref="PasswordLoginHandler"/>); staff accounts never get a usable code here.
/// </summary>
/// <param name="Channel">How to deliver the code.</param>
/// <param name="Destination">The email address or phone number to deliver to.</param>
public sealed record RequestOtpCommand(OtpChannel Channel, string Destination);
