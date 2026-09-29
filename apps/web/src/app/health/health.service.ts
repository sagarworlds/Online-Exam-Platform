import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { catchError, map, Observable, of, startWith } from 'rxjs';
import { environment } from '../../environments/environment';

/**
 * Result of checking the API's health, for display while the check is in flight
 * or has settled. A flat shape (rather than a discriminated union) so the
 * template can read `reason` directly without needing `@switch` to narrow it.
 */
export interface HealthStatus {
  state: 'checking' | 'healthy' | 'unhealthy';
  reason: string | null;
}

/**
 * Calls the API's health check endpoint. Exists mainly to prove, at a glance,
 * that this app and the .NET backend are wired together correctly — the first
 * thing worth verifying in a brand-new full-stack scaffold.
 */
@Injectable({ providedIn: 'root' })
export class HealthService {
  private readonly http = inject(HttpClient);

  /** Emits 'checking' immediately, then the outcome of calling GET /v1/health. */
  checkHealth(): Observable<HealthStatus> {
    return this.http.get(`${environment.apiBaseUrl}/v1/health`, { responseType: 'text' }).pipe(
      map((): HealthStatus => ({ state: 'healthy', reason: null })),
      catchError((error: unknown) => of<HealthStatus>({ state: 'unhealthy', reason: describe(error) })),
      startWith<HealthStatus>({ state: 'checking', reason: null }),
    );
  }
}

function describe(error: unknown): string {
  return error instanceof Error ? error.message : 'Unknown error';
}
