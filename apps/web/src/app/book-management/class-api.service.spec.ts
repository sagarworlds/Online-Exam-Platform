import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../environments/environment';
import { ClassApiService } from './class-api.service';

describe('ClassApiService', () => {
  let service: ClassApiService;
  let httpMock: HttpTestingController;
  const base = `${environment.apiBaseUrl}/v1/classes`;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(ClassApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('lists the open classes by default, so the plain URL is all that is asked for', () => {
    service.list().subscribe();

    const req = httpMock.expectOne(base);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.keys()).toEqual([]);
  });

  it('asks for archived classes too when told to', () => {
    service.list(true).subscribe();

    const req = httpMock.expectOne((r) => r.url === base);
    expect(req.request.params.get('includeArchived')).toBe('true');
  });

  it('creates a class from its name', () => {
    service.create({ name: '4th' }).subscribe();

    const req = httpMock.expectOne(base);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ name: '4th' });
  });

  it('renames a class by id', () => {
    service.rename('k4', { name: 'Fourth' }).subscribe();

    const req = httpMock.expectOne(`${base}/k4`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ name: 'Fourth' });
  });

  it('archives and restores a class by id', () => {
    service.archive('k4').subscribe();
    expect(httpMock.expectOne(`${base}/k4/archive`).request.method).toBe('POST');

    service.restore('k4').subscribe();
    expect(httpMock.expectOne(`${base}/k4/restore`).request.method).toBe('POST');
  });
});
