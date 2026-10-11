import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { InstructionTemplateDto, InstructionTemplateRequest } from './instruction-template.models';

/** Thin HTTP wrapper over the API's /v1/instruction-templates routes (FR-41). */
@Injectable({ providedIn: 'root' })
export class InstructionTemplateApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/v1/instruction-templates`;

  /** Lists every template, by title. */
  list(): Observable<InstructionTemplateDto[]> {
    return this.http.get<InstructionTemplateDto[]>(this.baseUrl);
  }

  /** Creates a template. */
  create(request: InstructionTemplateRequest): Observable<InstructionTemplateDto> {
    return this.http.post<InstructionTemplateDto>(this.baseUrl, request);
  }

  /** Changes a template; exams that already copied its text keep it. */
  update(id: string, request: InstructionTemplateRequest): Observable<InstructionTemplateDto> {
    return this.http.put<InstructionTemplateDto>(`${this.baseUrl}/${id}`, request);
  }

  /** Deletes a template; exams that already copied its text keep it. */
  remove(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }
}
