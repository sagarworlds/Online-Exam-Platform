import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import {
  AcceptInviteRequest,
  CreateInviteRequest,
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

  generateCode(inviteId: string, expiryHours?: number): Observable<InviteCodeDto> {
    const request: GenerateInviteCodeRequest = { expiryHours };
    return this.http.post<InviteCodeDto>(`${this.apiUrl}/${inviteId}/codes`, request);
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
}
