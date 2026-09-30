import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { BatchApiService } from './batch-api.service';
import { environment } from '../../environments/environment';

describe('BatchApiService', () => {
  let service: BatchApiService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [BatchApiService],
    });

    service = TestBed.inject(BatchApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should create a batch', () => {
    const request = { examId: 'exam-123', name: 'Test Batch', description: 'Test', maxMembers: 50 };
    const userId = 'user-123';

    service.createBatch(request, userId).subscribe();

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/v1/batches`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body.name).toBe('Test Batch');
    expect(req.request.body.maxMembers).toBe(50);
  });

  it('should get batches', () => {
    service.getBatches().subscribe();

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/v1/batches`);
    expect(req.request.method).toBe('GET');
  });

  it('should activate a batch', () => {
    const batchId = 'batch-123';

    service.activateBatch(batchId).subscribe();

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/v1/batches/${batchId}/activate`);
    expect(req.request.method).toBe('POST');
  });
});
