import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import {
  MarkingSchemeRequest,
  ShuffleRequest,
  AttemptLimitRequest,
  ContentProtectionRequest,
  CreateExamRequest,
  DrawQuestionsRequest,
  DrawRuleDto,
  EditSectionRequest,
  ExamDto,
  ExamScopeRequest,
  ExamQuestionDto,
  ExamSectionDto,
  ResultReleaseRequest,
  ScheduleExamRequest,
  UpdateExamDetailsRequest,
} from './exam.models';

/** Thin HTTP wrapper over the ExamAuthoring module's /v1/exams endpoints. */
@Injectable({ providedIn: 'root' })
export class ExamApiService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiBaseUrl}/v1/exams`;

  // The API records the caller (from the access token) as the creator, so the request
  // carries no user id: a client-supplied one would be ignored (FR-2).
  createExam(request: CreateExamRequest): Observable<ExamDto> {
    return this.http.post<ExamDto>(this.apiUrl, request);
  }

  getExams(): Observable<ExamDto[]> {
    return this.http.get<ExamDto[]>(this.apiUrl);
  }

  getExamById(id: string): Observable<ExamDto> {
    return this.http.get<ExamDto>(`${this.apiUrl}/${id}`);
  }

  scheduleExam(examId: string, request: ScheduleExamRequest): Observable<ExamDto> {
    return this.http.put<ExamDto>(`${this.apiUrl}/${examId}/schedule`, request);
  }

  setScope(examId: string, scope: ExamScopeRequest): Observable<ExamDto> {
    return this.http.put<ExamDto>(`${this.apiUrl}/${examId}/scope`, scope);
  }

  /** Chooses when candidates may see which of their answers were right. Allowed after publishing too. */
  setResultRelease(examId: string, request: ResultReleaseRequest): Observable<ExamDto> {
    return this.http.put<ExamDto>(`${this.apiUrl}/${examId}/result-release`, request);
  }

  /** Sets the marks for correct, incorrect and unattempted questions. Draft exams only: later scoring must not change. */
  setMarkingScheme(examId: string, request: MarkingSchemeRequest): Observable<ExamDto> {
    return this.http.put<ExamDto>(`${this.apiUrl}/${examId}/marking-scheme`, request);
  }

  /** Chooses whether questions and options are shown shuffled. Draft exams only: the order an attempt shows depends on it. */
  setShuffle(examId: string, request: ShuffleRequest): Observable<ExamDto> {
    return this.http.put<ExamDto>(`${this.apiUrl}/${examId}/shuffle`, request);
  }

  /** Sets how many attempts every enrolled candidate has. Allowed after publishing too: lowering it never takes an attempt back. */
  setAttemptLimit(examId: string, request: AttemptLimitRequest): Observable<ExamDto> {
    return this.http.put<ExamDto>(`${this.apiUrl}/${examId}/attempt-limit`, request);
  }

  /** Turns the exam page's copy, paste, right-click and print protection on or off. Allowed after publishing too: it changes nothing asked or scored. */
  setContentProtection(examId: string, request: ContentProtectionRequest): Observable<ExamDto> {
    return this.http.put<ExamDto>(`${this.apiUrl}/${examId}/content-protection`, request);
  }

  /** Shows candidates which answers were right, for an exam set to manual release. Safe to repeat. */
  releaseResults(examId: string): Observable<ExamDto> {
    return this.http.post<ExamDto>(`${this.apiUrl}/${examId}/results/release`, {});
  }

  addSection(examId: string, name: string, timeSeconds?: number | null): Observable<ExamSectionDto> {
    return this.http.post<ExamSectionDto>(`${this.apiUrl}/${examId}/sections`, { name, timeSeconds: timeSeconds ?? null });
  }

  addQuestion(examId: string, sectionId: string, questionId: string): Observable<ExamQuestionDto> {
    return this.http.post<ExamQuestionDto>(`${this.apiUrl}/${examId}/sections/${sectionId}/questions`, { questionId });
  }

  /** Adds random bank questions that match the request to a section of a draft exam; all or none. Returns the ones added. */
  /** Adds a rule that draws {@link DrawQuestionsRequest.count} random questions for each candidate when they start. */
  addDrawRule(examId: string, sectionId: string, request: DrawQuestionsRequest): Observable<DrawRuleDto> {
    return this.http.post<DrawRuleDto>(`${this.apiUrl}/${examId}/sections/${sectionId}/draw-rules`, request);
  }

  removeDrawRule(examId: string, sectionId: string, ruleId: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${examId}/sections/${sectionId}/draw-rules/${ruleId}`);
  }

  drawQuestions(examId: string, sectionId: string, request: DrawQuestionsRequest): Observable<ExamQuestionDto[]> {
    return this.http.post<ExamQuestionDto[]>(`${this.apiUrl}/${examId}/sections/${sectionId}/questions/draw`, request);
  }

  /** Changes the name and description. Allowed after publishing too: it changes nothing that is asked or scored. */
  updateDetails(examId: string, request: UpdateExamDetailsRequest): Observable<ExamDto> {
    return this.http.put<ExamDto>(`${this.apiUrl}/${examId}/details`, request);
  }

  /** Renames a section of a draft exam and sets its time limit. */
  editSection(examId: string, sectionId: string, request: EditSectionRequest): Observable<void> {
    return this.http.put<void>(`${this.apiUrl}/${examId}/sections/${sectionId}`, request);
  }

  /** Removes a section and the places its questions held from a draft exam; the questions stay in the bank. */
  removeSection(examId: string, sectionId: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${examId}/sections/${sectionId}`);
  }

  /** Takes a question out of a section of a draft exam; `questionId` is its id in the question bank. */
  removeQuestion(examId: string, sectionId: string, questionId: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${examId}/sections/${sectionId}/questions/${questionId}`);
  }

  /** Deletes a draft exam that no invitation or batch refers to. */
  deleteExam(examId: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${examId}`);
  }

  publish(examId: string): Observable<ExamDto> {
    return this.http.post<ExamDto>(`${this.apiUrl}/${examId}/publish`, {});
  }
}
