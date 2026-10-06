import { HttpClient, HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { environment } from '../../environments/environment';
import {
  CreateQuestionRequest,
  ExportedFile,
  ImportQuestionsResult,
  QuestionFileFormat,
  FileQuestionsRequest,
  FileQuestionsResult,
  QuestionDto,
  QuestionFilter,
  UpdateQuestionRequest,
} from './question.models';

/** Thin HTTP wrapper over the QuestionBank module's /v1/questions endpoints. */
@Injectable({ providedIn: 'root' })
export class QuestionApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/v1/questions`;

  /** One page of the newest questions. `skip` leaves out that many of the newest, to reach a later page. */
  list(filter: QuestionFilter = {}, skip = 0): Observable<QuestionDto[]> {
    // Only what is set goes on the URL, so an unfiltered list is the plain /v1/questions it always was.
    const params: Record<string, string | boolean> = {};
    if (filter.bookId) params['bookId'] = filter.bookId;
    if (filter.chapterId) params['chapterId'] = filter.chapterId;
    if (filter.unfiled) params['unfiled'] = true;
    if (filter.difficulty) params['difficulty'] = filter.difficulty;
    if (filter.topic) params['topic'] = filter.topic;
    if (filter.search) params['q'] = filter.search;
    if (skip > 0) params['skip'] = String(skip);
    return this.http.get<QuestionDto[]>(this.baseUrl, { params });
  }

  /** Every topic in use, once each, alphabetically, for the topic filter and for suggestions. */
  topics(): Observable<string[]> {
    return this.http.get<string[]>(`${this.baseUrl}/topics`);
  }

  get(id: string): Observable<QuestionDto> {
    return this.http.get<QuestionDto>(`${this.baseUrl}/${id}`);
  }

  create(request: CreateQuestionRequest): Observable<QuestionDto> {
    return this.http.post<QuestionDto>(this.baseUrl, request);
  }

  /** Replaces the question's content. Once candidates have answered it the API refuses anything but a wording change. */
  update(id: string, request: UpdateQuestionRequest): Observable<QuestionDto> {
    return this.http.put<QuestionDto>(`${this.baseUrl}/${id}`, request);
  }

  /** Deletes a question that no exam holds; the API refuses one that is in use. */
  remove(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }

  /** Creates the questions a file holds; `content` is text for CSV and JSON and base64 for Excel. Rows that fail are reported, not fatal. */
  import(format: QuestionFileFormat, content: string): Observable<ImportQuestionsResult> {
    return this.http.post<ImportQuestionsResult>(`${this.baseUrl}/import`, { format, content });
  }

  /** Downloads the questions the filter matches in a file format. */
  export(filter: QuestionFilter, format: QuestionFileFormat): Observable<ExportedFile> {
    const params: Record<string, string | boolean> = { format };
    if (filter.bookId) params['bookId'] = filter.bookId;
    if (filter.chapterId) params['chapterId'] = filter.chapterId;
    if (filter.unfiled) params['unfiled'] = true;
    if (filter.difficulty) params['difficulty'] = filter.difficulty;
    if (filter.topic) params['topic'] = filter.topic;
    if (filter.search) params['q'] = filter.search;
    return this.http.get(`${this.baseUrl}/export`, { params, responseType: 'blob', observe: 'response' }).pipe(
      map((response: HttpResponse<Blob>) => ({
        blob: response.body as Blob,
        fileName: `questions.${format}`,
        skipped: Number(response.headers.get('X-Questions-Skipped') ?? 0),
      })),
    );
  }

  /** Files questions under a chapter, all of them or none. */
  file(request: FileQuestionsRequest): Observable<FileQuestionsResult> {
    return this.http.post<FileQuestionsResult>(`${this.baseUrl}/placement`, request);
  }
}
