import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../environments/environment';
import { ProctoringApiService } from './proctoring-api.service';

describe('ProctoringApiService', () => {
  let api: ProctoringApiService;
  let httpMock: HttpTestingController;
  const base = `${environment.apiBaseUrl}/v1/proctoring`;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(ProctoringApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('reads one page of an exam queue for the chosen view', () => {
    api.listRiskFlags('e1', 'open', 2, 25).subscribe();

    const req = httpMock.expectOne((r) => r.url === `${base}/exams/e1/risk-flags`);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('filter')).toBe('open');
    expect(req.request.params.get('page')).toBe('2');
    expect(req.request.params.get('pageSize')).toBe('25');
    req.flush({ examId: 'e1', examName: 'Maths', filter: 'open', page: 2, pageSize: 25, total: 0, items: [] });
  });

  it('asks the server to score an exam', () => {
    api.runRiskScan('e1').subscribe();

    const req = httpMock.expectOne(`${base}/exams/e1/risk-scan`);
    expect(req.request.method).toBe('POST');
    req.flush({ examId: 'e1', scored: 0, flagged: 0, keptDecided: 0 });
  });

  it('sends a review with an optional note', () => {
    api.reviewRiskFlag('f1', null).subscribe();

    const req = httpMock.expectOne(`${base}/risk-flags/f1/review`);
    expect(req.request.body).toEqual({ note: null });
    req.flush(null);
  });

  it('sends a dismissal with its note', () => {
    api.dismissRiskFlag('f1', 'Power cut at the centre').subscribe();

    const req = httpMock.expectOne(`${base}/risk-flags/f1/dismiss`);
    expect(req.request.body).toEqual({ note: 'Power cut at the centre' });
    req.flush(null);
  });
});
