import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { BrandingDto, BrandingRequest } from './branding.models';

/** Thin HTTP wrapper over the API's /v1/branding routes (FR-41). Reading is open; changing needs the exam editor's permission. */
@Injectable({ providedIn: 'root' })
export class BrandingApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/v1/branding`;

  /** Reads the branding the candidate pages use. */
  get(): Observable<BrandingDto> {
    return this.http.get<BrandingDto>(this.baseUrl);
  }

  /** Saves the institute's name and colour; a blank field clears it. */
  save(request: BrandingRequest): Observable<BrandingDto> {
    return this.http.put<BrandingDto>(this.baseUrl, request);
  }

  /** Sends the chosen file as the logo. The API reads its format from the bytes and refuses anything it does not accept. */
  uploadLogo(file: Blob): Observable<BrandingDto> {
    return this.http.put<BrandingDto>(`${this.baseUrl}/logo`, file, { headers: { 'Content-Type': file.type } });
  }

  /** Removes the logo. */
  removeLogo(): Observable<BrandingDto> {
    return this.http.delete<BrandingDto>(`${this.baseUrl}/logo`);
  }

  /**
   * The address of the logo for these settings, or null when there is none. The time of the last change is in the address, so a replaced
   * logo is fetched again rather than shown from the browser's copy of the old one.
   */
  logoUrlFor(settings: BrandingDto): string | null {
    if (!settings.hasLogo) {
      return null;
    }

    return `${this.baseUrl}/logo?v=${encodeURIComponent(settings.updatedAtUtc ?? '')}`;
  }
}
