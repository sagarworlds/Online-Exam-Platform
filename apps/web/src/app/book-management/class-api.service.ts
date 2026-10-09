import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { ClassDto, ClassRequest } from './class.models';

/** Thin HTTP wrapper over the QuestionBank module's /v1/classes endpoints. Each change returns the class as it is afterwards. */
@Injectable({ providedIn: 'root' })
export class ClassApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/v1/classes`;

  list(includeArchived = false): Observable<ClassDto[]> {
    return this.http.get<ClassDto[]>(this.baseUrl, { params: includeArchived ? { includeArchived: true } : {} });
  }

  create(request: ClassRequest): Observable<ClassDto> {
    return this.http.post<ClassDto>(this.baseUrl, request);
  }

  rename(classId: string, request: ClassRequest): Observable<ClassDto> {
    return this.http.put<ClassDto>(`${this.baseUrl}/${classId}`, request);
  }

  archive(classId: string): Observable<ClassDto> {
    return this.http.post<ClassDto>(`${this.baseUrl}/${classId}/archive`, {});
  }

  restore(classId: string): Observable<ClassDto> {
    return this.http.post<ClassDto>(`${this.baseUrl}/${classId}/restore`, {});
  }
}
