import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom, toArray } from 'rxjs';
import { environment } from '../../environments/environment';
import { HealthService } from './health.service';

describe('HealthService', () => {
  let service: HealthService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(HealthService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('emits checking then healthy when the API responds successfully', async () => {
    const resultsPromise = firstValueFrom(service.checkHealth().pipe(toArray()));

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/v1/health`);
    req.flush('Healthy');

    expect(await resultsPromise).toEqual([
      { state: 'checking', reason: null },
      { state: 'healthy', reason: null },
    ]);
  });

  it('emits checking then unhealthy when the API request fails', async () => {
    const resultsPromise = firstValueFrom(service.checkHealth().pipe(toArray()));

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/v1/health`);
    req.flush('Service Unavailable', { status: 503, statusText: 'Service Unavailable' });

    const results = await resultsPromise;
    expect(results[0]).toEqual({ state: 'checking', reason: null });
    expect(results[1].state).toBe('unhealthy');
  });
});
