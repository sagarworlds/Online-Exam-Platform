import { HttpClient } from '@angular/common/http';
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
}
