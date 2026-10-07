import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../environments/environment';
import { CandidateApiService } from './candidate-api.service';
import { MyDisputeDto } from './candidate.models';

describe('CandidateApiService', () => {
  let service: CandidateApiService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(CandidateApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  describe('raiseDispute', () => {
    const created: MyDisputeDto = {
      id: 'd1',
      questionId: 'q2',
      reason: 'Paris is the capital of France',
      raisedAtUtc: '2026-10-06T10:00:00Z',
      status: 'Open',
      resolvedAtUtc: null,
      resolutionNote: null,
    };

    it('posts the question and the reason to the attempt’s disputes and returns the dispute', () => {
      let result: MyDisputeDto | undefined;

      service.raiseDispute('a1', 'q2', 'Paris is the capital of France').subscribe((dispute) => (result = dispute));

      const request = httpMock.expectOne(`${environment.apiBaseUrl}/v1/me/attempts/a1/disputes`);
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({ questionId: 'q2', reason: 'Paris is the capital of France' });
      request.flush(created, { status: 201, statusText: 'Created' });
      expect(result).toEqual(created);
    });

    it('passes the API’s refusal on to the caller', () => {
      let status: number | undefined;

      service.raiseDispute('a1', 'q2', 'Why').subscribe({ error: (error: { status: number }) => (status = error.status) });

      httpMock.expectOne(`${environment.apiBaseUrl}/v1/me/attempts/a1/disputes`).flush({ title: 'dispute_already_raised' }, { status: 409, statusText: 'Conflict' });
      expect(status).toBe(409);
    });
  });
});
