import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { CreateQuestionRequest, QuestionDto, QuestionFilter } from './question.models';

/** Thin HTTP wrapper over the QuestionBank module's /v1/questions endpoints. */
@Injectable({ providedIn: 'root' })
export class QuestionApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/v1/questions`;

  list(filter: QuestionFilter = {}): Observable<QuestionDto[]> {
    // Only what is set goes on the URL, so an unfiltered list is the plain /v1/questions it always was.
    const params: Record<string, string | boolean> = {};
    if (filter.bookId) params['bookId'] = filter.bookId;
    if (filter.chapterId) params['chapterId'] = filter.chapterId;
    if (filter.unfiled) params['unfiled'] = true;
    return this.http.get<QuestionDto[]>(this.baseUrl, { params });
  }

  create(request: CreateQuestionRequest): Observable<QuestionDto> {
    return this.http.post<QuestionDto>(this.baseUrl, request);
  }
}
