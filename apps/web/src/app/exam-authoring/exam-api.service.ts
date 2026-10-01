import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../environments/environment';
import { ExamDto, CreateExamRequest } from './exam.models';

@Injectable({ providedIn: 'root' })
export class ExamApiService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiBaseUrl}/v1/exams`;

  // The API records the caller (from the access token) as the creator, so the request
  // carries no user id: a client-supplied one would be ignored (FR-2).
  createExam(request: CreateExamRequest) {
    return this.http.post<ExamDto>(this.apiUrl, request);
  }

  getExams() {
    return this.http.get<ExamDto[]>(this.apiUrl);
  }

  getExamById(id: string) {
    return this.http.get<ExamDto>(`${this.apiUrl}/${id}`);
  }

  scheduleExam(examId: string, startTime: Date, endTime: Date, timeZone: string) {
    return this.http.put(`${this.apiUrl}/${examId}/schedule`, {
      scheduledStartTime: startTime,
      scheduledEndTime: endTime,
      timeZone,
    });
  }
}
