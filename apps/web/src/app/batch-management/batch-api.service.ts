import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../environments/environment';
import { BatchDto, CreateBatchRequest, BatchMemberDto, AddBatchMemberRequest } from './batch.models';

@Injectable({ providedIn: 'root' })
export class BatchApiService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiBaseUrl}/v1/batches`;

  createBatch(request: CreateBatchRequest, userId: string) {
    return this.http.post<BatchDto>(this.apiUrl, {
      ...request,
      createdBy: userId,
    });
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
