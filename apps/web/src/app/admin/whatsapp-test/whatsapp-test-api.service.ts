import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { WhatsAppDeliveryDto, WhatsAppSendRequest, WhatsAppSendResultDto, WhatsAppStatusDto } from './whatsapp-test.models';

/** Thin HTTP wrapper over the API's WhatsApp test routes (status, send one message, delivery report). */
@Injectable({ providedIn: 'root' })
export class WhatsAppTestApiService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiBaseUrl}/v1/admin/whatsapp`;

  /** How the platform's WhatsApp connection is set up, and what is missing. */
  status(): Observable<WhatsAppStatusDto> {
    return this.http.get<WhatsAppStatusDto>(`${this.url}/status`);
  }

  /** Sends one test message. A delivery problem is not an HTTP error: it comes back as `sent: false` with a `failure`. */
  send(request: WhatsAppSendRequest): Observable<WhatsAppSendResultDto> {
    return this.http.post<WhatsAppSendResultDto>(`${this.url}/messages`, request);
  }

  /** What delivery reports have said about a message so far; `NotReported` until the first one arrives. */
  delivery(messageId: string): Observable<WhatsAppDeliveryDto> {
    return this.http.get<WhatsAppDeliveryDto>(`${this.url}/messages/${encodeURIComponent(messageId)}`);
  }
}
