import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { RiskFlagFilterName, RiskFlagQueue, RiskScanResult } from './proctoring.models';

/** Thin HTTP wrapper over the Proctoring module's staff routes: the risk score and the review of flagged attempts (FR-27). */
@Injectable({ providedIn: 'root' })
export class ProctoringApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/v1/proctoring`;

  /** One page of an exam's review queue for the given view, highest score first. */
  listRiskFlags(examId: string, filter: RiskFlagFilterName, page = 1, pageSize = 50): Observable<RiskFlagQueue> {
    return this.http.get<RiskFlagQueue>(`${this.baseUrl}/exams/${examId}/risk-flags`, {
      params: { filter, page, pageSize },
    });
  }

  /** Scores the exam's finished attempts. Only the reviewer asks for this; nothing scores attempts on its own. */
  runRiskScan(examId: string): Observable<RiskScanResult> {
    return this.http.post<RiskScanResult>(`${this.baseUrl}/exams/${examId}/risk-scan`, null);
  }

  /** Marks a flagged attempt reviewed. The note is optional. */
  reviewRiskFlag(flagId: string, note: string | null): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/risk-flags/${flagId}/review`, { note });
  }

  /** Dismisses a flag. The note is required: the API refuses a dismissal without one. */
  dismissRiskFlag(flagId: string, note: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/risk-flags/${flagId}/dismiss`, { note });
  }
}
