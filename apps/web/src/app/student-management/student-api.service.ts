import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { FindStudentsRequest, StudentDto, StudentSummaryDto, UpdateStudentRequest, UpdateStudentResult } from './student.models';

/** Thin HTTP wrapper over the Identity module's /v1/admin/students endpoints. */
@Injectable({ providedIn: 'root' })
export class StudentApiService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiBaseUrl}/v1/admin/students`;

  /** The first 200 students by name; `search` narrows them to those whose name, e-mail address or phone number contains it. */
  getStudents(search = ''): Observable<StudentSummaryDto[]> {
    const text = search.trim();
    const params = text === '' ? new HttpParams() : new HttpParams().set('search', text);
    return this.http.get<StudentSummaryDto[]>(this.apiUrl, { params });
  }

  /** Which of the addresses (at most 200) belong to students; the others have no account yet, or belong to staff. */
  findByEmail(emails: string[]): Observable<StudentSummaryDto[]> {
    const request: FindStudentsRequest = { emails };
    return this.http.post<StudentSummaryDto[]>(`${this.apiUrl}/lookup`, request);
  }

  getStudent(studentId: string): Observable<StudentDto> {
    return this.http.get<StudentDto>(`${this.apiUrl}/${studentId}`);
  }

  updateStudent(studentId: string, request: UpdateStudentRequest): Observable<UpdateStudentResult> {
    return this.http.put<UpdateStudentResult>(`${this.apiUrl}/${studentId}`, request);
  }
}
