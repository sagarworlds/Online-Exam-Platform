import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import {
  AcceptInviteRequest,
  ChangeInviteEmailRequest,
  ChangeInviteEmailResult,
  CreateInviteRequest,
  DirectSignInRequest,
  DirectSignInResult,
  GenerateInviteCodeRequest,
  InviteCodeDto,
  InviteDto,
} from './invite.models';

/** Thin HTTP wrapper over the Invite module's /v1/invites endpoints. */
@Injectable({ providedIn: 'root' })
export class InviteApiService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiBaseUrl}/v1/invites`;

  // The API records the caller (from the access token) as the inviter, so the request
  // carries no user id: a client-supplied one would be ignored (FR-2).
  createInvite(request: CreateInviteRequest): Observable<InviteDto> {
    return this.http.post<InviteDto>(this.apiUrl, request);
  }

  getInvites(): Observable<InviteDto[]> {
    return this.http.get<InviteDto[]>(this.apiUrl);
  }

  /** Makes another single-use code; with `directSignIn` its link signs the invited person in, which needs the `invite.direct-link` permission. */
  generateCode(inviteId: string, expiryHours?: number, directSignIn = false): Observable<InviteCodeDto> {
    const request: GenerateInviteCodeRequest = directSignIn ? { expiryHours, directSignIn } : { expiryHours };
    return this.http.post<InviteCodeDto>(`${this.apiUrl}/${inviteId}/codes`, request);
  }

  /**
   * Opens a direct sign-in link: no one is signed in yet, so the code is the only proof. The API accepts the invitation, signs the invited
   * student in and names the exam to open. It works once.
   */
  directSignIn(code: string): Observable<DirectSignInResult> {
    const request: DirectSignInRequest = { code };
    return this.http.post<DirectSignInResult>(`${this.apiUrl}/direct-sign-in`, request);
  }

  /** The signed-in candidate redeems the code from their invitation link; the API checks it is their address. */
  acceptInvite(code: string): Observable<InviteDto> {
    const request: AcceptInviteRequest = { code };
    return this.http.post<InviteDto>(`${this.apiUrl}/accept`, request);
  }

  declineInvite(inviteId: string): Observable<void> {
    return this.http.post<void>(`${this.apiUrl}/${inviteId}/decline`, {});
  }

  revokeInvite(inviteId: string): Observable<void> {
    return this.http.post<void>(`${this.apiUrl}/${inviteId}/revoke`, {});
  }

  /** Moves every invitation sent to `currentEmail` (in any status) to `newEmail`; only the account holding the new address can accept one. */
  changeEmail(currentEmail: string, newEmail: string): Observable<ChangeInviteEmailResult> {
    const request: ChangeInviteEmailRequest = { currentEmail, newEmail };
    return this.http.post<ChangeInviteEmailResult>(`${this.apiUrl}/change-email`, request);
  }
}
