import { HttpClient, HttpContext, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { ApiActivityService } from './api-activity.service';
import { SILENT_ACTIVITY, apiActivityInterceptor } from './api-activity.interceptor';

describe('apiActivityInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let begin: ReturnType<typeof vi.spyOn>;
  let end: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors([apiActivityInterceptor])), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
    const activity = TestBed.inject(ApiActivityService);
    begin = vi.spyOn(activity, 'begin');
    end = vi.spyOn(activity, 'end');
  });

  it('reports a call starting, and finishing when it is answered', () => {
    http.get('/x').subscribe();
    expect(begin).toHaveBeenCalledTimes(1);
    expect(end).not.toHaveBeenCalled();

    httpMock.expectOne('/x').flush({});
    expect(end).toHaveBeenCalledTimes(1);
  });

  it('reports a call finishing when it fails', () => {
    http.get('/x').subscribe({ error: () => undefined });

    httpMock.expectOne('/x').flush('nope', { status: 500, statusText: 'Server Error' });

    expect(end).toHaveBeenCalledTimes(1);
  });

  it('reports a call finishing when the caller gives up on it', () => {
    const subscription = http.get('/x').subscribe();

    subscription.unsubscribe();

    expect(end).toHaveBeenCalledTimes(1);
  });

  it('leaves out a call that asks to be silent', () => {
    http.get('/x', { context: new HttpContext().set(SILENT_ACTIVITY, true) }).subscribe();
    httpMock.expectOne('/x').flush({});

    expect(begin).not.toHaveBeenCalled();
    expect(end).not.toHaveBeenCalled();
  });
});
