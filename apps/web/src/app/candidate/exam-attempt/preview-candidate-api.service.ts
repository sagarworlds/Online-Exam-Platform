import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { EMPTY, Observable, of } from 'rxjs';
import { environment } from '../../../environments/environment';
import { CandidateApiService } from '../candidate-api.service';
import { AttemptDto } from '../candidate.models';

/**
 * Stands in for the candidate's API on the preview route (FR-15), so the exam page runs unchanged while a staff member looks at an
 * exam. The questions come from the staff-only preview endpoint, and everything the page would save is accepted and dropped: a choice,
 * a mark, a section change. Nothing is stored, so the preview cannot create an attempt or touch a score.
 *
 * The page asks for the "attempt" by the id in its route, which on the preview route is the exam's id.
 */
@Injectable()
export class PreviewCandidateApiService extends CandidateApiService {
  private readonly previewHttp = inject(HttpClient);

  override getAttempt(examId: string): Observable<AttemptDto> {
    return this.previewHttp.get<AttemptDto>(`${environment.apiBaseUrl}/v1/exams/${examId}/preview`);
  }

  override saveAnswer(): Observable<void> {
    return of(undefined);
  }

  override saveAnswers(): Observable<void> {
    return of(undefined);
  }

  override clearAnswer(): Observable<void> {
    return of(undefined);
  }

  override markForReview(): Observable<void> {
    return of(undefined);
  }

  override unmarkForReview(): Observable<void> {
    return of(undefined);
  }

  override moveToSection(): Observable<void> {
    return of(undefined);
  }

  // The page does not ask for these in preview (there is no attempt to pause, warn or end, and nobody to count leaving the page),
  // but if it ever did, nothing is sent.
  override getAttemptStatus(): Observable<never> {
    return EMPTY;
  }

  override reportFocusViolation(): Observable<never> {
    return EMPTY;
  }

  override submitAttempt(): Observable<never> {
    return EMPTY;
  }
}
