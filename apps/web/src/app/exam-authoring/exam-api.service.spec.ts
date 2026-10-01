import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { ExamApiService } from './exam-api.service';
import { environment } from '../../environments/environment';

describe('ExamApiService', () => {
  let service: ExamApiService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [ExamApiService],
    });

    service = TestBed.inject(ExamApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should create an exam without a createdBy field', () => {
    const request = { seriesId: '123', name: 'Test Exam', description: 'Test' };

    service.createExam(request).subscribe();

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/v1/exams`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body.name).toBe('Test Exam');
    expect(req.request.body.seriesId).toBe('123');
    expect('createdBy' in req.request.body).toBe(false);
  });

  it('should send a null seriesId through unchanged for a standalone exam', () => {
    service.createExam({ seriesId: null, name: 'Standalone' }).subscribe();

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/v1/exams`);
    expect(req.request.body.seriesId).toBeNull();
  });

  it('should get exams', () => {
    service.getExams().subscribe();

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/v1/exams`);
    expect(req.request.method).toBe('GET');
  });

  it('should get exam by id', () => {
    const examId = 'exam-123';

    service.getExamById(examId).subscribe();

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/v1/exams/${examId}`);
    expect(req.request.method).toBe('GET');
  });
});
