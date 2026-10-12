import { HttpClient, HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { CandidateAnalyticsDto, ExamItemAnalysisDto } from './analytics.models';

/** Thin HTTP wrapper over the Analytics module's routes (FR-36). */
@Injectable({ providedIn: 'root' })
export class AnalyticsApiService {
  private readonly http = inject(HttpClient);

  /**
   * The signed-in candidate's own score trend and results by section, from the results released to them. The API takes the candidate from
   * the sign-in, so nothing identifies the candidate in the request.
   */
  getMyAnalytics(): Observable<CandidateAnalyticsDto> {
    return this.http.get<CandidateAnalyticsDto>(`${environment.apiBaseUrl}/v1/me/analytics`);
  }

  /**
   * An exam's item analysis (FR-37): how each question performed among the released results. Staff only; the API refuses anyone without the
   * exam-management permission.
   */
  getItemAnalysis(examId: string): Observable<ExamItemAnalysisDto> {
    return this.http.get<ExamItemAnalysisDto>(`${environment.apiBaseUrl}/v1/exams/${examId}/analytics/items`);
  }

  /**
   * Downloads an exam's item analysis as CSV (FR-38). It is a POST because the server records the export in the audit log, so the response
   * carries the file's headers, which name the file, along with its bytes.
   */
  exportItemAnalysisCsv(examId: string): Observable<HttpResponse<Blob>> {
    return this.download(`${environment.apiBaseUrl}/v1/exams/${examId}/analytics/items/exports`);
  }

  /** Downloads an exam's item analysis as a PDF report (FR-38). Staff only, and recorded in the audit log like the CSV. */
  exportItemAnalysisPdf(examId: string): Observable<HttpResponse<Blob>> {
    return this.download(`${environment.apiBaseUrl}/v1/exams/${examId}/analytics/items/exports/pdf`);
  }

  /** Downloads the signed-in candidate's own analytics as CSV (FR-38). The candidate comes from the sign-in, as on the page. */
  exportMyAnalyticsCsv(): Observable<HttpResponse<Blob>> {
    return this.download(`${environment.apiBaseUrl}/v1/me/analytics/exports/csv`);
  }

  /** Downloads the signed-in candidate's own analytics as a PDF report (FR-38). */
  exportMyAnalyticsPdf(): Observable<HttpResponse<Blob>> {
    return this.download(`${environment.apiBaseUrl}/v1/me/analytics/exports/pdf`);
  }

  /** A POST with no body that reads the file's bytes and headers, the shape every export shares. */
  private download(url: string): Observable<HttpResponse<Blob>> {
    return this.http.post(url, null, { observe: 'response', responseType: 'blob' });
  }
}
