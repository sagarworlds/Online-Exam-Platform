/** A candidate code that is still usable, as the API returns it (unmasked: only staff holding `identity.otp.read` can ask). */
export interface OutstandingOtp {
  challengeId: string;
  purpose: 'Login' | 'Registration';
  channel: 'Email' | 'Sms';
  destination: string;
  code: string;
  expiresAtUtc: string;
}
