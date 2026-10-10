import { HttpClient, HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { environment } from '../../environments/environment';
import type { ContentEncryptionStatus } from './question.models';
import {
  AddTranslationRequest,
  AnswerKeyCorrectionResult,
  CreateQuestionRequest,
  DuplicateQuestion,
  ExportedFile,
  ImportQuestionsResult,
  QuestionFileFormat,
  ReviewEntry,
  ReviewResult,
  ReviewStep,
  FileQuestionsRequest,
  FileQuestionsResult,
  QuestionDto,
  QuestionFilter,
  QuestionStatistics,
  QuestionTranslation,
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
    if (filter.classId) params['classId'] = filter.classId;
    if (filter.bookId) params['bookId'] = filter.bookId;
    if (filter.chapterId) params['chapterId'] = filter.chapterId;
    if (filter.unfiled) params['unfiled'] = true;
    if (filter.difficulty) params['difficulty'] = filter.difficulty;
    if (filter.topic) params['topic'] = filter.topic;
    if (filter.search) params['q'] = filter.search;
    if (filter.status) params['status'] = filter.status;
    if (filter.language) params['language'] = filter.language;
    if (skip > 0) params['skip'] = String(skip);
    return this.http.get<QuestionDto[]>(this.baseUrl, { params });
  }

  /** How much of the bank's content is encrypted at rest, for the admin status (#57). */
  encryptionStatus(): Observable<ContentEncryptionStatus> {
    return this.http.get<ContentEncryptionStatus>(`${this.baseUrl}/encryption-status`);
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

  /**
   * Corrects which options are right in a question candidates may already have answered (FR-31): the one change an answered question
   * allows. Every submitted attempt that held the question is scored again under the corrected key and its candidate is shown the reason,
   * and the open disputes of the question are accepted. Naming the options the key already has changes nothing.
   * A text question has no options to name: its key is `acceptedAnswers`, which replaces the accepted answers it has.
   */
  correctAnswerKey(id: string, correctOptionIds: string[], reason: string, acceptedAnswers?: string[]): Observable<AnswerKeyCorrectionResult> {
    return this.http.post<AnswerKeyCorrectionResult>(`${this.baseUrl}/${id}/correct-answer-key`, { correctOptionIds, reason, acceptedAnswers });
  }

  /** Deletes a question that no exam holds; the API refuses one that is in use. */
  remove(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }

  /** Creates the questions a file holds; `content` is text for CSV and JSON and base64 for Excel. Rows that fail are reported, not fatal. */
  import(format: QuestionFileFormat, content: string, allowDuplicates = false): Observable<ImportQuestionsResult> {
    // Sent only when set, so an ordinary import is the request it always was.
    return this.http.post<ImportQuestionsResult>(`${this.baseUrl}/import`, { format, content, ...(allowDuplicates ? { allowDuplicates } : {}) });
  }

  /** The questions already in the bank with the same wording as this one, those with the same options first (FR-9). */
  duplicates(text: string, options: string[], excludeQuestionId?: string): Observable<DuplicateQuestion[]> {
    return this.http.post<DuplicateQuestion[]>(`${this.baseUrl}/duplicates`, { text, options, excludeQuestionId });
  }

  /** The question and its linked translations, one per language (FR-10). */
  translations(id: string): Observable<QuestionTranslation[]> {
    return this.http.get<QuestionTranslation[]>(`${this.baseUrl}/${id}/translations`);
  }

  /** Adds a translation of a question; it is a draft of its own, with the answer key of the question translated (FR-10). */
  addTranslation(id: string, request: AddTranslationRequest): Observable<QuestionDto> {
    return this.http.post<QuestionDto>(`${this.baseUrl}/${id}/translations`, request);
  }

  /** The exams that hold a question and how candidates have answered it (FR-9). */
  statistics(id: string): Observable<QuestionStatistics> {
    return this.http.get<QuestionStatistics>(`${this.baseUrl}/${id}/statistics`);
  }

  /** Downloads the questions the filter matches in a file format. */
  export(filter: QuestionFilter, format: QuestionFileFormat): Observable<ExportedFile> {
    const params: Record<string, string | boolean> = { format };
    if (filter.classId) params['classId'] = filter.classId;
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

  /** A question's review thread, oldest first. */
  reviewLog(id: string): Observable<ReviewEntry[]> {
    return this.http.get<ReviewEntry[]>(`${this.baseUrl}/${id}/review-log`);
  }

  /** Takes a review step (put forward, approve, send back, retire, restore); the comment is required to send a question back. */
  reviewStep(id: string, step: ReviewStep, comment: string): Observable<ReviewResult> {
    return this.http.post<ReviewResult>(`${this.baseUrl}/${id}/${step}`, { comment });
  }

  /** Adds a comment to a question's review thread. */
  comment(id: string, comment: string): Observable<ReviewResult> {
    return this.http.post<ReviewResult>(`${this.baseUrl}/${id}/comments`, { comment });
  }

  /** Files questions under a chapter, all of them or none. */
  file(request: FileQuestionsRequest): Observable<FileQuestionsResult> {
    return this.http.post<FileQuestionsResult>(`${this.baseUrl}/placement`, request);
  }
}
