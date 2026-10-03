import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { AttemptDto, AttemptReviewDto, MyAttemptRequestDto, MyExamDto } from './candidate.models';

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

  /** Asks an administrator for one more attempt at an exam; they decide, and the answer shows on the exams page. */
  requestAttempt(examId: string, message: string | null): Observable<MyAttemptRequestDto> {
    return this.http.post<MyAttemptRequestDto>(`${this.baseUrl}/exams/${examId}/attempt-requests`, { message });
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

  /** Saves the set of options chosen for a multiple-answer question, replacing any earlier choice. At least one is needed; to take an answer back, clear it. */
  saveAnswers(attemptId: string, questionId: string, optionIds: string[]): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/attempts/${attemptId}/answers/${questionId}`, { optionIds });
  }

  /** Takes back the option chosen for one question, so it counts as unanswered again. Safe to repeat. */
  clearAnswer(attemptId: string, questionId: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/attempts/${attemptId}/answers/${questionId}`);
  }

  /** Marks one question of an open attempt for review. Safe to repeat. */
  markForReview(attemptId: string, questionId: string): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/attempts/${attemptId}/marks/${questionId}`, null);
  }

  /** Takes the review mark off one question. Safe to repeat. */
  unmarkForReview(attemptId: string, questionId: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/attempts/${attemptId}/marks/${questionId}`);
  }

  /** Moves on to a later section of an exam that locks sections; the section left cannot be returned to. */
  moveToSection(attemptId: string, sectionId: string): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/attempts/${attemptId}/section/${sectionId}`, null);
  }

  /** Ends the attempt and returns it with its score. Safe to repeat. */
  submitAttempt(attemptId: string): Observable<AttemptDto> {
    return this.http.post<AttemptDto>(`${this.baseUrl}/attempts/${attemptId}/submit`, null);
  }
}
