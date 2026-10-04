import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { OutstandingOtp } from './otp-codes.models';

/** Thin HTTP wrapper over the Identity module's route for the codes candidates are waiting for. */
@Injectable({ providedIn: 'root' })
export class OtpCodesApiService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiBaseUrl}/v1/admin/otp-codes`;

  /** The unspent codes, optionally only for destinations containing `destination`. Every call is audited server-side. */
  list(destination: string | null): Observable<OutstandingOtp[]> {
    return this.http.get<OutstandingOtp[]>(this.url, { params: destination ? { destination } : {} });
  }
}
