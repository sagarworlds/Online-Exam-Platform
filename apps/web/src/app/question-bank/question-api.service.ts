import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { CreateQuestionRequest, QuestionDto } from './question.models';

/** Thin HTTP wrapper over the QuestionBank module's /v1/questions endpoints. */
@Injectable({ providedIn: 'root' })
export class QuestionApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/v1/questions`;

  list(): Observable<QuestionDto[]> {
    return this.http.get<QuestionDto[]>(this.baseUrl);
  }

  create(request: CreateQuestionRequest): Observable<QuestionDto> {
    return this.http.post<QuestionDto>(this.baseUrl, request);
  }
}
