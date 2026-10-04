namespace ExamPlatform.Modules.Identity.Application.Dtos;

/// <summary>A candidate code that is still usable, as shown to an administrator.</summary>
/// <param name="ChallengeId">The challenge's identifier.</param>
/// <param name="Purpose">What the code authorizes: <c>Login</c> or <c>Registration</c>.</param>
/// <param name="Channel">How the code was sent: <c>Email</c> or <c>Sms</c>.</param>
/// <param name="Destination">The email address or phone number the code was sent to, unmasked.</param>
/// <param name="Code">The plaintext code.</param>
/// <param name="ExpiresAtUtc">When the code stops working.</param>
public sealed record OutstandingOtpDto(
    Guid ChallengeId,
    string Purpose,
    string Channel,
    string Destination,
    string Code,
    DateTime ExpiresAtUtc);
