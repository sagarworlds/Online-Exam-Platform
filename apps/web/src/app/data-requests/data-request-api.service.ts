import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { DataRequestDto, OpenDataRequestDto, RaiseDataRequest, ResolveDataRequest } from './data-request.models';

/** Thin HTTP wrapper over the Identity module's data-request routes (FR-48): a person's own requests, and the staff queue. */
@Injectable({ providedIn: 'root' })
export class DataRequestApiService {
  private readonly http = inject(HttpClient);
  private readonly mineUrl = `${environment.apiBaseUrl}/v1/me/data-requests`;
  private readonly staffUrl = `${environment.apiBaseUrl}/v1/data-requests`;

  /** Every request the signed-in person has made, newest first. */
  listMine(): Observable<DataRequestDto[]> {
    return this.http.get<DataRequestDto[]>(this.mineUrl);
  }

  /** Sends a request. `409 data_request_already_open` when the person has an open one of this kind. */
  raise(request: RaiseDataRequest): Observable<DataRequestDto> {
    return this.http.post<DataRequestDto>(this.mineUrl, request);
  }

  /** Every request still waiting for an answer, the one due soonest first, so the overdue ones come first (`consent.manage`). */
  listOpen(): Observable<OpenDataRequestDto[]> {
    return this.http.get<OpenDataRequestDto[]>(`${this.staffUrl}/open`);
  }

  /** Records staff's answer to one request, once (`consent.manage`). A refusal needs a note, and the candidate sees the note. */
  resolve(id: string, request: ResolveDataRequest): Observable<DataRequestDto> {
    return this.http.post<DataRequestDto>(`${this.staffUrl}/${id}/resolve`, request);
  }
}
