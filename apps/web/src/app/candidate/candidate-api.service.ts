import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { AttemptDto, AttemptReviewDto, MyExamDto } from './candidate.models';

/** Thin HTTP wrapper over the ExamRuntime module's candidate-facing /v1/me endpoints. */
@Injectable({ providedIn: 'root' })
export class CandidateApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/v1/me`;

  listMyExams(): Observable<MyExamDto[]> {
    return this.http.get<MyExamDto[]>(`${this.baseUrl}/exams`);
  }

  /** Starts the candidate's attempt at an exam, or returns the one they already have (resume). */
  startAttempt(examId: string): Observable<AttemptDto> {
    return this.http.post<AttemptDto>(`${this.baseUrl}/exams/${examId}/attempts`, null);
  }

  getAttempt(attemptId: string): Observable<AttemptDto> {
    return this.http.get<AttemptDto>(`${this.baseUrl}/attempts/${attemptId}`);
  }

  /** Which answers were right, for a submitted attempt whose answers have been released; the API refuses it otherwise. */
  getAttemptReview(attemptId: string): Observable<AttemptReviewDto> {
    return this.http.get<AttemptReviewDto>(`${this.baseUrl}/attempts/${attemptId}/review`);
  }

  /** Saves (or changes) the option chosen for one question of an open attempt. */
  saveAnswer(attemptId: string, questionId: string, optionId: string): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/attempts/${attemptId}/answers/${questionId}`, { optionId });
  }

  /** Ends the attempt and returns it with its score. Safe to repeat. */
  submitAttempt(attemptId: string): Observable<AttemptDto> {
    return this.http.post<AttemptDto>(`${this.baseUrl}/attempts/${attemptId}/submit`, null);
  }
}
