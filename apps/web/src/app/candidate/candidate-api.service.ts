import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { MyExamDto } from './candidate.models';

/** Thin HTTP wrapper over the ExamRuntime module's candidate-facing /v1/me endpoints. */
@Injectable({ providedIn: 'root' })
export class CandidateApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/v1/me`;

  listMyExams(): Observable<MyExamDto[]> {
    return this.http.get<MyExamDto[]>(`${this.baseUrl}/exams`);
  }
}
