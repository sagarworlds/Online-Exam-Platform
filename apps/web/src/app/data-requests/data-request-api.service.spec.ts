import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../environments/environment';
import { DataRequestApiService } from './data-request-api.service';

describe('DataRequestApiService', () => {
  let service: DataRequestApiService;
  let httpMock: HttpTestingController;
  const mine = `${environment.apiBaseUrl}/v1/me/data-requests`;
  const staff = `${environment.apiBaseUrl}/v1/data-requests`;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(DataRequestApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it("reads the signed-in person's own requests", () => {
    service.listMine().subscribe();

    const req = httpMock.expectOne(mine);
    expect(req.request.method).toBe('GET');
  });

  it('sends a request with its kind and details, and no account: the account comes from the token', () => {
    service.raise({ kind: 'Erasure', details: 'Please remove my phone number.' }).subscribe();

    const req = httpMock.expectOne(mine);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ kind: 'Erasure', details: 'Please remove my phone number.' });
  });

  it('reads the staff queue of open requests', () => {
    service.listOpen().subscribe();

    const req = httpMock.expectOne(`${staff}/open`);
    expect(req.request.method).toBe('GET');
  });

  it("records staff's answer against the request's own address, with the outcome and the note", () => {
    service.resolve('r-1', { outcome: 'Rejected', note: 'Exam results must be kept for three years.' }).subscribe();

    const req = httpMock.expectOne(`${staff}/r-1/resolve`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ outcome: 'Rejected', note: 'Exam results must be kept for three years.' });
  });
});
