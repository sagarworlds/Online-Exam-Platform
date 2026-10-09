import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../environments/environment';
import { I18nService } from './i18n.service';
import { languageInterceptor } from './language.interceptor';

describe('languageInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors([languageInterceptor])), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    localStorage.clear();
  });

  it('tells the API the language the user chose, so questions come back in it', async () => {
    await TestBed.inject(I18nService).setLanguage('hi');

    http.get(`${environment.apiBaseUrl}/v1/me/exams`).subscribe();

    expect(httpMock.expectOne(`${environment.apiBaseUrl}/v1/me/exams`).request.headers.get('Accept-Language')).toBe('hi');
  });

  it('follows a change of language on the very next request', async () => {
    const i18n = TestBed.inject(I18nService);
    await i18n.setLanguage('hi');
    http.get(`${environment.apiBaseUrl}/a`).subscribe();
    await i18n.setLanguage('mr');
    http.get(`${environment.apiBaseUrl}/b`).subscribe();

    const languages = httpMock.match(() => true).map((r) => r.request.headers.get('Accept-Language'));

    expect(languages).toEqual(['hi', 'mr']);
  });

  it('does not hand the choice to any other site', async () => {
    await TestBed.inject(I18nService).setLanguage('hi');

    http.get('https://example.com/data').subscribe();

    expect(httpMock.expectOne('https://example.com/data').request.headers.has('Accept-Language')).toBe(false);
  });
});
