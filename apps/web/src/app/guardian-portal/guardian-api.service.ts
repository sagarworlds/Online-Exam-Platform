import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../environments/environment';
import { GuardianDto, CreateGuardianRequest, GuardianLinkDto, LinkCandidateRequest, VerifyGuardianLinkRequest } from './guardian.models';

@Injectable({ providedIn: 'root' })
export class GuardianApiService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiBaseUrl}/v1/guardians`;

  registerGuardian(request: CreateGuardianRequest) {
    return this.http.post<GuardianDto>(this.apiUrl, request);
  }

  getGuardian(id: string) {
    return this.http.get<GuardianDto>(`${this.apiUrl}/${id}`);
  }

  linkCandidate(guardianId: string, request: LinkCandidateRequest) {
    return this.http.post<GuardianLinkDto>(`${this.apiUrl}/${guardianId}/links`, request);
  }

  verifyLink(request: VerifyGuardianLinkRequest) {
    return this.http.post<void>(`${this.apiUrl}/links/verify`, request);
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
