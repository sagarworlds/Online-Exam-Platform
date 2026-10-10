import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../environments/environment';
import {
  GuardianDto,
  CreateGuardianRequest,
  GuardianLinkDto,
  LinkCandidateRequest,
  LinkCandidateResponse,
} from './guardian.models';

@Injectable({ providedIn: 'root' })
export class GuardianApiService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiBaseUrl}/v1/guardians`;
  // Outside the guardians API on purpose: confirming a link needs no sign-in, only the code from the guardian's e-mail.
  private verifyUrl = `${environment.apiBaseUrl}/v1/guardian-links/verify`;

  registerGuardian(request: CreateGuardianRequest) {
    return this.http.post<GuardianDto>(this.apiUrl, request);
  }

  /** Finds the guardian registered with an e-mail address, so a candidate can be linked to them without knowing their id. */
  findGuardianByEmail(email: string) {
    return this.http.get<GuardianDto>(this.apiUrl, { params: { email } });
  }

  linkCandidate(guardianId: string, request: LinkCandidateRequest) {
    return this.http.post<LinkCandidateResponse>(`${this.apiUrl}/${guardianId}/links`, request);
  }

  /** Confirms a candidate link with the one-time code from the guardian's e-mail. */
  verifyLink(token: string) {
    return this.http.post<GuardianLinkDto>(this.verifyUrl, { token });
  }

  revokeLink(guardianId: string, candidateId: string) {
    return this.http.delete<void>(`${this.apiUrl}/${guardianId}/links/${candidateId}`);
  }

  unlinkCandidate(guardianId: string, candidateId: string) {
    return this.http.delete<void>(`${this.apiUrl}/${guardianId}/candidates/${candidateId}`);
  }

  getGuardianLinks(guardianId: string) {
    return this.http.get<GuardianLinkDto[]>(`${this.apiUrl}/${guardianId}/links`);
  }
}
