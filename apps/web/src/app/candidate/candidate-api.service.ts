import { HttpClient, HttpContext } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { SILENT_ACTIVITY } from '../shared/api-activity/api-activity.interceptor';
import {
  AttemptDto,
  AttemptReviewDto,
  AttemptStatusDto,
  FocusViolationKind,
  FocusViolationResultDto,
  IssueCategory,
  MyAttemptRequestDto,
  MyDisputeDto,
  MyExamDto,
  MyIssueReportDto,
} from './candidate.models';

/** Thin HTTP wrapper over the ExamRuntime module's candidate-facing /v1/me endpoints. */
@Injectable({ providedIn: 'root' })
export class CandidateApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/v1/me`;

  /**
   * For a change that already shows on screen and is saved in the background (an exam answer, a review mark).
   * Such a call stays out of the waiting indicator, which would otherwise flash on every click mid-exam.
   */
  private static background(): { context: HttpContext } {
    return { context: new HttpContext().set(SILENT_ACTIVITY, true) };
  }

  listMyExams(): Observable<MyExamDto[]> {
    return this.http.get<MyExamDto[]>(`${this.baseUrl}/exams`);
  }

  /**
   * Starts the candidate's attempt at an exam, or returns the one they already have (resume). A new attempt is refused unless the
   * candidate acknowledged the instructions, which the instructions page asks for; resuming needs no acknowledgment.
   */
  startAttempt(examId: string, instructionsAcknowledged = false): Observable<AttemptDto> {
    return this.http.post<AttemptDto>(`${this.baseUrl}/exams/${examId}/attempts`, { instructionsAcknowledged });
  }

  /** Asks an administrator for one more attempt at an exam; they decide, and the answer shows on the exams page. */
  requestAttempt(examId: string, message: string | null): Observable<MyAttemptRequestDto> {
    return this.http.post<MyAttemptRequestDto>(`${this.baseUrl}/exams/${examId}/attempt-requests`, { message });
  }

  /**
   * Reads an attempt. With `lowBandwidth` the questions come without their pictures, which are fetched one at a time with
   * `getQuestionPicture` when the candidate asks for them (FR-53).
   */
  getAttempt(attemptId: string, lowBandwidth = false): Observable<AttemptDto> {
    return this.http.get<AttemptDto>(`${this.baseUrl}/attempts/${attemptId}`, lowBandwidth ? { params: { lite: true } } : {});
  }

  /** One picture of a question in an open attempt, for a page that was sent the attempt without them (FR-53). The browser keeps it for the sitting. */
  getQuestionPicture(attemptId: string, questionId: string, key: string): Observable<Blob> {
    return this.http.get(`${this.baseUrl}/attempts/${attemptId}/questions/${questionId}/pictures/${key}`, { responseType: 'blob' });
  }

  /** Which answers were right, for a submitted attempt whose answers have been released; the API refuses it otherwise. */
  getAttemptReview(attemptId: string): Observable<AttemptReviewDto> {
    return this.http.get<AttemptReviewDto>(`${this.baseUrl}/attempts/${attemptId}/review`);
  }

  /**
   * Disputes the answer key of one question of a result the candidate can review (FR-31). Each question can be disputed once, and staff's
   * answer is final; the API refuses a dispute once the time allowed since the result was released has passed.
   */
  raiseDispute(attemptId: string, questionId: string, reason: string): Observable<MyDisputeDto> {
    return this.http.post<MyDisputeDto>(`${this.baseUrl}/attempts/${attemptId}/disputes`, { questionId, reason });
  }

  /**
   * Reports a problem from inside the exam being sat (FR-42): with a question, the page, or anything else. The attempt carries on and
   * the clock keeps running; an attempt may report only so many problems, and a submitted one none.
   */
  reportIssue(attemptId: string, category: IssueCategory, message: string, questionId: string | null): Observable<MyIssueReportDto> {
    return this.http.post<MyIssueReportDto>(`${this.baseUrl}/attempts/${attemptId}/issues`, { category, message, questionId });
  }

  /** Saves (or changes) the option chosen for one question of an open attempt. */
  saveAnswer(attemptId: string, questionId: string, optionId: string): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/attempts/${attemptId}/answers/${questionId}`, { optionId }, CandidateApiService.background());
  }

  /** Saves the set of options chosen for a multiple-answer question, replacing any earlier choice. At least one is needed; to take an answer back, clear it. */
  saveAnswers(attemptId: string, questionId: string, optionIds: string[]): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/attempts/${attemptId}/answers/${questionId}`, { optionIds }, CandidateApiService.background());
  }

  /** Takes back the option chosen for one question, so it counts as unanswered again. Safe to repeat. */
  clearAnswer(attemptId: string, questionId: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/attempts/${attemptId}/answers/${questionId}`, CandidateApiService.background());
  }

  /** Marks one question of an open attempt for review. Safe to repeat. */
  markForReview(attemptId: string, questionId: string): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/attempts/${attemptId}/marks/${questionId}`, null, CandidateApiService.background());
  }

  /** Takes the review mark off one question. Safe to repeat. */
  unmarkForReview(attemptId: string, questionId: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/attempts/${attemptId}/marks/${questionId}`, CandidateApiService.background());
  }

  /** Moves on to a later section of an exam that locks sections; the section left cannot be returned to. */
  moveToSection(attemptId: string, sectionId: string): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/attempts/${attemptId}/section/${sectionId}`, null);
  }

  /**
   * The exam page's heartbeat (FR-29): whether the attempt is still open or paused, its deadline as it stands now, and any warnings
   * administrators sent. Silent, because it runs every few seconds and a waiting indicator each time would be noise.
   */
  getAttemptStatus(attemptId: string): Observable<AttemptStatusDto> {
    return this.http.get<AttemptStatusDto>(`${this.baseUrl}/attempts/${attemptId}/status`, CandidateApiService.background());
  }

  /**
   * Tells the server the candidate left the exam page (FR-22). Silent, like an answer: the page already shows the warning, and a
   * waiting indicator coming up as the candidate returns would only be noise. The reply says how many they have used and whether
   * the attempt has ended.
   */
  reportFocusViolation(attemptId: string, kind: FocusViolationKind): Observable<FocusViolationResultDto> {
    return this.http.post<FocusViolationResultDto>(`${this.baseUrl}/attempts/${attemptId}/focus-violations`, { kind }, CandidateApiService.background());
  }

  /** Ends the attempt and returns it with its score. Safe to repeat. */
  submitAttempt(attemptId: string): Observable<AttemptDto> {
    return this.http.post<AttemptDto>(`${this.baseUrl}/attempts/${attemptId}/submit`, null);
  }
}
