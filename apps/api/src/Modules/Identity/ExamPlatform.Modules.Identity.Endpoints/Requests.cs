namespace ExamPlatform.Modules.Identity.Endpoints;

/// <summary>Request body for <c>POST /v1/auth/otp/request</c>.</summary>
public sealed record RequestOtpRequest(string Channel, string Destination);

/// <summary>Request body for <c>POST /v1/auth/otp/verify</c>.</summary>
public sealed record VerifyOtpRequest(Guid OtpChallengeId, string Code);

/// <summary>Request body for <c>POST /v1/auth/register</c>.</summary>
public sealed record RegisterCandidateRequest(
    string? Email, string? PhoneNumber, DateOnly DateOfBirth, string DisplayName, string OtpChannel);

/// <summary>Request body for <c>POST /v1/auth/login</c>.</summary>
public sealed record PasswordLoginRequest(string Email, string Password);

/// <summary>Request body for <c>POST /v1/auth/password-reset/request</c>.</summary>
public sealed record RequestPasswordResetRequest(string Email);

/// <summary>Request body for <c>POST /v1/auth/password-reset/reset</c>.</summary>
public sealed record ResetPasswordRequest(Guid PasswordResetTokenId, string Token, string NewPassword);

/// <summary>Request body for <c>PUT /v1/me/profile</c>.</summary>
public sealed record UpdateProfileRequest(string DisplayName);

/// <summary>Request body for <c>POST /v1/admin/users/{userId}/roles</c>.</summary>
public sealed record AssignRoleRequest(Guid RoleId);
