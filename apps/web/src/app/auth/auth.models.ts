/** Delivery channel for a one-time-password code (mirrors OtpChannel). */
export type OtpChannel = 'Email' | 'Sms';

export interface RequestOtpRequest {
  channel: OtpChannel;
  destination: string;
}

export interface RequestOtpResponse {
  otpChallengeId: string;
}

/**
 * Router navigation state the login and registration pages pass to /verify-otp.
 * The destination travels here, never as a query parameter, so the email or
 * phone number stays out of the URL, browser history and server/proxy logs (NFR-6).
 */
export interface VerifyOtpNavigationState {
  destination: string;
}

export interface VerifyOtpRequest {
  otpChallengeId: string;
  code: string;
}

export interface RegisterCandidateRequest {
  email: string | null;
  phoneNumber: string | null;
  dateOfBirth: string;
  displayName: string;
  otpChannel: OtpChannel;
}

export interface RegisterCandidateResponse {
  otpChallengeId: string;
}

export interface PasswordLoginRequest {
  email: string;
  password: string;
}

/**
 * Result of an authentication step. Either a completed login (accessToken and
 * sessionId set) or a pending second factor (otpChallengeId set instead) —
 * mirrors the backend's AuthResult.
 */
export interface AuthResult {
  requiresTwoFactor: boolean;
  accessToken: string | null;
  sessionId: string | null;
  otpChallengeId: string | null;
}

export interface RequestPasswordResetRequest {
  email: string;
}

export interface ResetPasswordRequest {
  passwordResetTokenId: string;
  token: string;
  newPassword: string;
}

export interface UpdateProfileRequest {
  displayName: string;
}

export interface UserProfileDto {
  userId: string;
  email: string | null;
  phoneNumber: string | null;
  displayName: string;
  status: string;
  roles: readonly string[];
}
