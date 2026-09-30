import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import {
  AuthResult,
  PasswordLoginRequest,
  RegisterCandidateRequest,
  RegisterCandidateResponse,
  RequestOtpRequest,
  RequestOtpResponse,
  RequestPasswordResetRequest,
  ResetPasswordRequest,
  UpdateProfileRequest,
  UserProfileDto,
  VerifyOtpRequest,
} from './auth.models';

/** Thin HTTP wrapper over the Identity module's /v1/auth/* and /v1/me/* endpoints. */
@Injectable({ providedIn: 'root' })
export class AuthApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/v1`;

  requestOtp(request: RequestOtpRequest): Observable<RequestOtpResponse> {
    return this.http.post<RequestOtpResponse>(`${this.baseUrl}/auth/otp/request`, request);
  }

  verifyOtp(request: VerifyOtpRequest): Observable<AuthResult> {
    return this.http.post<AuthResult>(`${this.baseUrl}/auth/otp/verify`, request);
  }

  register(request: RegisterCandidateRequest): Observable<RegisterCandidateResponse> {
    return this.http.post<RegisterCandidateResponse>(`${this.baseUrl}/auth/register`, request);
  }

  login(request: PasswordLoginRequest): Observable<AuthResult> {
    return this.http.post<AuthResult>(`${this.baseUrl}/auth/login`, request);
  }

  requestPasswordReset(request: RequestPasswordResetRequest): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/auth/password-reset/request`, request);
  }

  resetPassword(request: ResetPasswordRequest): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/auth/password-reset/reset`, request);
  }

  /**
   * Revokes the caller's current session on the server (FR-4), so the bearer
   * token stops working everywhere, not just in this browser. Completes with
   * no value on 204; errors with an HttpErrorResponse otherwise (e.g. 401 when
   * the session was already revoked or superseded).
   */
  logout(): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/auth/logout`, null);
  }

  getProfile(): Observable<UserProfileDto> {
    return this.http.get<UserProfileDto>(`${this.baseUrl}/me/profile`);
  }

  updateProfile(request: UpdateProfileRequest): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/me/profile`, request);
  }
}
