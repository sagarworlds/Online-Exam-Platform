import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../environments/environment';
import { BatchDto, CreateBatchRequest, BatchMemberDto, AddBatchMemberRequest } from './batch.models';

@Injectable({ providedIn: 'root' })
export class BatchApiService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiBaseUrl}/v1/batches`;

  // The API records the caller (from the access token) as the creator, so the request
  // carries no user id: a client-supplied one would be ignored (FR-2).
  createBatch(request: CreateBatchRequest) {
    return this.http.post<BatchDto>(this.apiUrl, request);
  }

  getBatches() {
    return this.http.get<BatchDto[]>(this.apiUrl);
  }

  getBatchById(id: string) {
    return this.http.get<BatchDto>(`${this.apiUrl}/${id}`);
  }

  addMember(batchId: string, request: AddBatchMemberRequest) {
    return this.http.post<void>(`${this.apiUrl}/${batchId}/members`, request);
  }

  getBatchMembers(batchId: string) {
    return this.http.get<BatchMemberDto[]>(`${this.apiUrl}/${batchId}/members`);
  }

  activateBatch(batchId: string) {
    return this.http.post<void>(`${this.apiUrl}/${batchId}/activate`, {});
  }

  closeBatch(batchId: string) {
    return this.http.post<void>(`${this.apiUrl}/${batchId}/close`, {});
  }
}
