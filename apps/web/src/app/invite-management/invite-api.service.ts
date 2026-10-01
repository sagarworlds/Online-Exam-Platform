import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../environments/environment';
import { InviteDto, CreateInviteRequest, InviteCodeDto, GenerateInviteCodeRequest, AcceptInviteRequest } from './invite.models';

@Injectable({ providedIn: 'root' })
export class InviteApiService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiBaseUrl}/v1/invites`;

  // The API records the caller (from the access token) as the creator, so the request
  // carries no user id: a client-supplied one would be ignored (FR-2).
  createInvite(request: CreateInviteRequest) {
    return this.http.post<InviteDto>(this.apiUrl, request);
  }

  getInvites() {
    return this.http.get<InviteDto[]>(this.apiUrl);
  }

  getInviteById(id: string) {
    return this.http.get<InviteDto>(`${this.apiUrl}/${id}`);
  }

  generateCode(inviteId: string, expiryHours?: number) {
    const request: GenerateInviteCodeRequest = { expiryHours };
    return this.http.post<InviteCodeDto>(`${this.apiUrl}/${inviteId}/codes`, request);
  }

  acceptInvite(inviteId: string, inviteCodeId: string) {
    const request: AcceptInviteRequest = { inviteCodeId };
    return this.http.post<void>(`${this.apiUrl}/${inviteId}/accept`, request);
  }

  declineInvite(inviteId: string) {
    return this.http.post<void>(`${this.apiUrl}/${inviteId}/decline`, {});
  }

  revokeInvite(inviteId: string) {
    return this.http.post<void>(`${this.apiUrl}/${inviteId}/revoke`, {});
  }
}
