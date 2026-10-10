import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { BrandingService } from './branding.service';

describe('BrandingService', () => {
  let httpMock: HttpTestingController;
  let service: BrandingService;

  const isBrandingRead = (request: { method: string; url: string }) =>
    request.method === 'GET' && request.url.endsWith('/v1/branding');

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    httpMock = TestBed.inject(HttpTestingController);
    service = TestBed.inject(BrandingService);
  });

  afterEach(() => {
    httpMock.verify();
    document.documentElement.style.removeProperty('--brand-primary');
    document.documentElement.style.removeProperty('--brand-on-primary');
    document.documentElement.style.removeProperty('--brand-primary-soft');
  });

  it('applies the institute’s colour and name once the start-up read answers', () => {
    service.load();
    httpMock.expectOne(isBrandingRead).flush({
      instituteName: 'Riverside Academy',
      primaryColour: '#1A56DB',
      hasLogo: true,
      updatedAtUtc: '2026-10-10T05:00:00Z',
    });

    expect(service.state()).toBe('ready');
    expect(service.instituteName()).toBe('Riverside Academy');
    expect(document.documentElement.style.getPropertyValue('--brand-primary')).toBe('#1A56DB');
    expect(service.logoUrl()).toContain('/v1/branding/logo?v=');
  });

  it('keeps the platform’s default look and reports the failure when the read fails', () => {
    // The exam pages must still open when the branding cannot be read, so the failure leaves the default look in place.
    service.load();
    httpMock.expectOne(isBrandingRead).flush('unavailable', { status: 503, statusText: 'Service Unavailable' });

    expect(service.state()).toBe('failed');
    expect(service.settings()).toBeNull();
    expect(document.documentElement.style.getPropertyValue('--brand-primary')).toBe('');
    expect(service.logoUrl()).toBeNull();
  });

  it('removes the institute’s colour again when the branding is saved without one', () => {
    service.apply({ instituteName: null, primaryColour: '#1A56DB', hasLogo: false, updatedAtUtc: null });

    service.apply({ instituteName: null, primaryColour: null, hasLogo: false, updatedAtUtc: null });

    expect(document.documentElement.style.getPropertyValue('--brand-primary')).toBe('');
    expect(service.instituteName()).toBeNull();
  });
});
