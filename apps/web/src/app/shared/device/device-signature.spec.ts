import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { DEVICE_SIGNATURE_HEADER, deviceSignatureInterceptor } from './device-signature.interceptor';
import { deviceSignature, resetDeviceSignature } from './device-signature';

describe('deviceSignature', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    resetDeviceSignature();
  });

  it('is 32 lowercase hex characters, which is what the API accepts', () => {
    expect(deviceSignature()).toMatch(/^[0-9a-f]{32}$/);
  });

  it('is the same every time on one device', () => {
    expect(deviceSignature()).toBe(deviceSignature());
    const first = deviceSignature();
    resetDeviceSignature();
    expect(deviceSignature()).toBe(first);
  });

  it('differs when the browser, language or screen differs', () => {
    const base = deviceSignature();

    resetDeviceSignature();
    vi.spyOn(navigator, 'userAgent', 'get').mockReturnValue('SomeOtherBrowser/1.0');
    const otherBrowser = deviceSignature();

    resetDeviceSignature();
    vi.restoreAllMocks();
    vi.spyOn(navigator, 'language', 'get').mockReturnValue('hi-IN');
    const otherLanguage = deviceSignature();

    resetDeviceSignature();
    vi.restoreAllMocks();
    vi.spyOn(screen, 'width', 'get').mockReturnValue(1234);
    const otherScreen = deviceSignature();

    expect(new Set([base, otherBrowser, otherLanguage, otherScreen]).size).toBe(4);
  });
});

describe('deviceSignatureInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors([deviceSignatureInterceptor])), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('sends the signature to the platform API', () => {
    http.get(`${environment.apiBaseUrl}/v1/me/exams`).subscribe();

    const request = httpMock.expectOne(`${environment.apiBaseUrl}/v1/me/exams`);
    expect(request.request.headers.get(DEVICE_SIGNATURE_HEADER)).toBe(deviceSignature());
    request.flush([]);
  });

  it('keeps one a caller set itself', () => {
    http.get(`${environment.apiBaseUrl}/v1/me/exams`, { headers: { [DEVICE_SIGNATURE_HEADER]: 'custom' } }).subscribe();

    const request = httpMock.expectOne(`${environment.apiBaseUrl}/v1/me/exams`);
    expect(request.request.headers.get(DEVICE_SIGNATURE_HEADER)).toBe('custom');
    request.flush([]);
  });

  it('sends nothing to anywhere else', () => {
    http.get('https://elsewhere.example.com/data').subscribe();

    const request = httpMock.expectOne('https://elsewhere.example.com/data');
    expect(request.request.headers.has(DEVICE_SIGNATURE_HEADER)).toBe(false);
    request.flush({});
  });
});
