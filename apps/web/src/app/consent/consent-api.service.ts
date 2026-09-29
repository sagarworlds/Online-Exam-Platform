import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { ConsentPurpose, ConsentRecordDto, ConsentStatusDto, RecordConsentRequest } from './consent.models';

/** Thin HTTP wrapper over the Consent module's /v1/consent endpoints. */
@Injectable({ providedIn: 'root' })
export class ConsentApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/v1/consent`;

  getStatus(subjectId: string, purpose: ConsentPurpose): Observable<ConsentStatusDto> {
    const params = new HttpParams().set('subjectId', subjectId).set('purpose', purpose);
    return this.http.get<ConsentStatusDto>(`${this.baseUrl}/status`, { params });
  }

  grant(request: RecordConsentRequest): Observable<ConsentRecordDto> {
    return this.http.post<ConsentRecordDto>(`${this.baseUrl}/`, request);
  }

  withdraw(consentRecordId: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${consentRecordId}`);
  }
}
