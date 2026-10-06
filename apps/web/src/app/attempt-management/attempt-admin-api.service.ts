import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { AttemptSummaryDto } from '../candidate/candidate.models';
import { AttemptPaperDto, AttemptRequestFilterStatus, AttemptRequestRow, ExamAttemptsDto, ExamCandidateDto } from './attempt-admin.models';

/** Thin HTTP wrapper over the ExamRuntime module's staff routes: an exam's candidates and their attempts. */
@Injectable({ providedIn: 'root' })
export class AttemptAdminApiService {
  private readonly http = inject(HttpClient);
  private readonly examsUrl = `${environment.apiBaseUrl}/v1/exams`;
  private readonly requestsUrl = `${environment.apiBaseUrl}/v1/attempt-requests`;

  getExamAttempts(examId: string): Observable<ExamAttemptsDto> {
    return this.http.get<ExamAttemptsDto>(`${this.examsUrl}/${examId}/attempts`);
  }

  /** The questions one attempt consisted of, with those drawn for the candidate marked. */
  getAttemptPaper(examId: string, attemptId: string): Observable<AttemptPaperDto> {
    return this.http.get<AttemptPaperDto>(`${this.examsUrl}/${examId}/attempts/${attemptId}/paper`);
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

  /** Sends the candidate a warning, which their exam page shows within seconds (FR-29). */
  warnAttempt(examId: string, attemptId: string, message: string): Observable<AttemptSummaryDto> {
    return this.http.post<AttemptSummaryDto>(`${this.examsUrl}/${examId}/attempts/${attemptId}/warn`, { message });
  }

  /** Pauses an attempt in progress: the candidate cannot answer and the clock stops (FR-29). */
  pauseAttempt(examId: string, attemptId: string): Observable<AttemptSummaryDto> {
    return this.http.post<AttemptSummaryDto>(`${this.examsUrl}/${examId}/attempts/${attemptId}/pause`, null);
  }

  /** Resumes a paused attempt; its deadline moves later by the time it was paused (FR-29). */
  resumeAttempt(examId: string, attemptId: string): Observable<AttemptSummaryDto> {
    return this.http.post<AttemptSummaryDto>(`${this.examsUrl}/${examId}/attempts/${attemptId}/resume`, null);
  }

  /** Ends an attempt early, scored with the answers saved so far; the candidate is shown the reason (FR-29). */
  terminateAttempt(examId: string, attemptId: string, reason: string): Observable<AttemptSummaryDto> {
    return this.http.post<AttemptSummaryDto>(`${this.examsUrl}/${examId}/attempts/${attemptId}/terminate`, { reason });
  }

  /** Invalidates a finished attempt's result so it no longer counts; the candidate is shown the reason instead of a score (FR-29). */
  invalidateAttempt(examId: string, attemptId: string, reason: string): Observable<AttemptSummaryDto> {
    return this.http.post<AttemptSummaryDto>(`${this.examsUrl}/${examId}/attempts/${attemptId}/invalidate`, { reason });
  }

  /** Gives one enrolled candidate one more attempt; answers with how they now stand. The giver is the signed-in user. */
  grantExtraAttempt(examId: string, candidateId: string, reason: string | null): Observable<ExamCandidateDto> {
    return this.http.post<ExamCandidateDto>(`${this.examsUrl}/${examId}/candidates/${candidateId}/extra-attempts`, { reason });
  }
}
