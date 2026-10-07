import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../environments/environment';
import { AttemptAdminApiService } from './attempt-admin-api.service';
import { DisputeRow } from './attempt-admin.models';

describe('AttemptAdminApiService', () => {
  let service: AttemptAdminApiService;
  let httpMock: HttpTestingController;

  const dispute = (overrides: Partial<DisputeRow> = {}): DisputeRow => ({
    id: 'd1',
    examId: 'e1',
    examName: 'Maths Final',
    attemptId: 'a1',
    attemptNumber: 1,
    candidateId: 'c1',
    candidateEmail: 'amy@example.com',
    questionId: 'q1',
    questionText: '<p>Capital of France?</p>',
    reason: 'Paris is the capital',
    raisedAtUtc: '2026-10-05T05:00:00Z',
    status: 'Open',
    resolvedAtUtc: null,
    resolutionNote: null,
    ...overrides,
  });

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(AttemptAdminApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  describe('listDisputes', () => {
    it('asks for the open disputes unless told otherwise', () => {
      let result: DisputeRow[] | undefined;

      service.listDisputes().subscribe((rows) => (result = rows));

      const request = httpMock.expectOne((r) => r.url === `${environment.apiBaseUrl}/v1/disputes`);
      expect(request.request.method).toBe('GET');
      expect(request.request.params.get('status')).toBe('open');
      request.flush([dispute()]);
      expect(result).toEqual([dispute()]);
    });

    it.each(['open', 'accepted', 'rejected'] as const)('puts the %s status in the query', (status) => {
      service.listDisputes(status).subscribe();

      const request = httpMock.expectOne((r) => r.url === `${environment.apiBaseUrl}/v1/disputes`);
      expect(request.request.params.get('status')).toBe(status);
      request.flush([]);
    });
  });

  describe('rejectDispute', () => {
    it('posts the explanation to the dispute’s reject route and returns the dispute', () => {
      const rejected = dispute({ status: 'Rejected', resolvedAtUtc: '2026-10-06T12:00:00Z', resolutionNote: 'Rome was the capital then' });
      let result: DisputeRow | undefined;

      service.rejectDispute('d1', 'Rome was the capital then').subscribe((row) => (result = row));

      const request = httpMock.expectOne(`${environment.apiBaseUrl}/v1/disputes/d1/reject`);
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({ note: 'Rome was the capital then' });
      request.flush(rejected);
      expect(result).toEqual(rejected);
    });

    it('passes the API’s refusal on to the caller', () => {
      let status: number | undefined;

      service.rejectDispute('d1', 'No').subscribe({ error: (error: { status: number }) => (status = error.status) });

      httpMock.expectOne(`${environment.apiBaseUrl}/v1/disputes/d1/reject`).flush({ title: 'dispute_not_open' }, { status: 409, statusText: 'Conflict' });
      expect(status).toBe(409);
    });
  });
});
