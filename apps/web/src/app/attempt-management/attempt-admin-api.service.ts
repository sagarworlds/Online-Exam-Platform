import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { AttemptRequestFilterStatus, AttemptRequestRow, ExamAttemptsDto, ExamCandidateDto } from './attempt-admin.models';

/** Thin HTTP wrapper over the ExamRuntime module's staff routes: an exam's candidates and their attempts. */
@Injectable({ providedIn: 'root' })
export class AttemptAdminApiService {
  private readonly http = inject(HttpClient);
  private readonly examsUrl = `${environment.apiBaseUrl}/v1/exams`;
  private readonly requestsUrl = `${environment.apiBaseUrl}/v1/attempt-requests`;

  getExamAttempts(examId: string): Observable<ExamAttemptsDto> {
    return this.http.get<ExamAttemptsDto>(`${this.examsUrl}/${examId}/attempts`);
  }

  /** The candidates' requests for another attempt with the given status, oldest first (waiting ones by default). */
  listAttemptRequests(status: AttemptRequestFilterStatus = 'pending'): Observable<AttemptRequestRow[]> {
    return this.http.get<AttemptRequestRow[]>(this.requestsUrl, { params: { status } });
  }

  /** Gives the candidate the attempt they asked for. */
  approveAttemptRequest(requestId: string): Observable<AttemptRequestRow> {
    return this.http.post<AttemptRequestRow>(`${this.requestsUrl}/${requestId}/approve`, null);
  }

  /** Turns the request down; the note, if any, is shown to the candidate. */
  declineAttemptRequest(requestId: string, note: string | null): Observable<AttemptRequestRow> {
    return this.http.post<AttemptRequestRow>(`${this.requestsUrl}/${requestId}/decline`, { note });
  }

  /** Gives one enrolled candidate one more attempt; answers with how they now stand. The giver is the signed-in user. */
  grantExtraAttempt(examId: string, candidateId: string, reason: string | null): Observable<ExamCandidateDto> {
    return this.http.post<ExamCandidateDto>(`${this.examsUrl}/${examId}/candidates/${candidateId}/extra-attempts`, { reason });
  }
}
