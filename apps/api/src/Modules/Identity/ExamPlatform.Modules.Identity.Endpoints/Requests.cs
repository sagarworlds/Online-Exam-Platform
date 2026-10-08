namespace ExamPlatform.Modules.Identity.Endpoints;

/// <summary>Request body for <c>POST /v1/auth/otp/request</c>.</summary>
public sealed record RequestOtpRequest(string Channel, string Destination);

/// <summary>Request body for <c>POST /v1/auth/otp/verify</c>.</summary>
public sealed record VerifyOtpRequest(Guid OtpChallengeId, string Code);

// DateOfBirth is nullable so that an omitted date arrives as null and is refused; as a plain
// DateOnly it bound to 0001-01-01, which reads as an adult and skipped minor detection (FR-43).
/// <summary>Request body for <c>POST /v1/auth/register</c>.</summary>
public sealed record RegisterCandidateRequest(
    string? Email, string? PhoneNumber, DateOnly? DateOfBirth, string DisplayName, string OtpChannel);

/// <summary>Request body for <c>POST /v1/auth/login</c>.</summary>
public sealed record PasswordLoginRequest(string Email, string Password);

/// <summary>Request body for <c>POST /v1/auth/password-reset/request</c>.</summary>
public sealed record RequestPasswordResetRequest(string Email);

/// <summary>Request body for <c>POST /v1/auth/password-reset/reset</c>.</summary>
public sealed record ResetPasswordRequest(Guid PasswordResetTokenId, string Token, string NewPassword);

/// <summary>Request body for <c>POST /v1/admin/whatsapp/messages</c>.</summary>
/// <param name="PhoneNumber">The recipient, as typed; the default country code is added when it has none.</param>
/// <param name="Mode"><c>Text</c> (a message the administrator wrote) or <c>SignInTemplate</c> (the sign-in code template, which works for anyone).</param>
/// <param name="Message">The text, for <c>Text</c>; at most 1000 characters.</param>
public sealed record SendWhatsAppTestRequest(string? PhoneNumber, string? Mode, string? Message);

/// <summary>Request body for <c>PUT /v1/me/profile</c>.</summary>
public sealed record UpdateProfileRequest(string DisplayName);

/// <summary>Request body for <c>POST /v1/admin/users/{userId}/roles</c>.</summary>
public sealed record AssignRoleRequest(Guid RoleId);
