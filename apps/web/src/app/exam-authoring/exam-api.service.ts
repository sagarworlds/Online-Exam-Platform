import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import {
  CreateExamRequest,
  ExamDto,
  ExamQuestionDto,
  ExamSectionDto,
  ScheduleExamRequest,
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

  addSection(examId: string, name: string, timeSeconds?: number | null): Observable<ExamSectionDto> {
    return this.http.post<ExamSectionDto>(`${this.apiUrl}/${examId}/sections`, { name, timeSeconds: timeSeconds ?? null });
  }

  addQuestion(examId: string, sectionId: string, questionId: string): Observable<ExamQuestionDto> {
    return this.http.post<ExamQuestionDto>(`${this.apiUrl}/${examId}/sections/${sectionId}/questions`, { questionId });
  }

  publish(examId: string): Observable<ExamDto> {
    return this.http.post<ExamDto>(`${this.apiUrl}/${examId}/publish`, {});
  }
}
