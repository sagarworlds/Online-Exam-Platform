import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { ExamAttemptsDto, ExamCandidateDto } from './attempt-admin.models';

/** Thin HTTP wrapper over the ExamRuntime module's staff routes: an exam's candidates and their attempts. */
@Injectable({ providedIn: 'root' })
export class AttemptAdminApiService {
  private readonly http = inject(HttpClient);
  private readonly examsUrl = `${environment.apiBaseUrl}/v1/exams`;

  getExamAttempts(examId: string): Observable<ExamAttemptsDto> {
    return this.http.get<ExamAttemptsDto>(`${this.examsUrl}/${examId}/attempts`);
  }

  /** Gives one enrolled candidate one more attempt; answers with how they now stand. The giver is the signed-in user. */
  grantExtraAttempt(examId: string, candidateId: string, reason: string | null): Observable<ExamCandidateDto> {
    return this.http.post<ExamCandidateDto>(`${this.examsUrl}/${examId}/candidates/${candidateId}/extra-attempts`, { reason });
  }
}
