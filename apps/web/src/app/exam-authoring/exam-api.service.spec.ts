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

  it('schedules an exam with a PUT to its schedule route', () => {
    const request = { scheduledStartTime: '2026-10-05T04:30:00.000Z', scheduledEndTime: '2026-10-05T07:30:00.000Z', durationMinutes: 90 };

    service.scheduleExam('exam-1', request).subscribe();

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/v1/exams/exam-1/schedule`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(request);
  });

  it('adds a section and a question to the right routes', () => {
    service.addSection('exam-1', 'Algebra').subscribe();
    const section = httpMock.expectOne(`${environment.apiBaseUrl}/v1/exams/exam-1/sections`);
    expect(section.request.method).toBe('POST');
    expect(section.request.body).toEqual({ name: 'Algebra', timeSeconds: null });

    service.addQuestion('exam-1', 's1', 'q1').subscribe();
    const question = httpMock.expectOne(`${environment.apiBaseUrl}/v1/exams/exam-1/sections/s1/questions`);
    expect(question.request.method).toBe('POST');
    expect(question.request.body).toEqual({ questionId: 'q1' });
  });

  it('draws random questions with a POST to the draw route', () => {
    service.drawQuestions('exam-1', 's1', { count: 3, difficulty: 'hard', topic: 'fractions' }).subscribe();

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/v1/exams/exam-1/sections/s1/questions/draw`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ count: 3, difficulty: 'hard', topic: 'fractions' });
  });

  it('adds and removes draw rules through the draw-rules routes', () => {
    service.addDrawRule('exam-1', 's1', { count: 2, difficulty: 'easy', topic: null }).subscribe();
    const add = httpMock.expectOne(`${environment.apiBaseUrl}/v1/exams/exam-1/sections/s1/draw-rules`);
    expect(add.request.method).toBe('POST');
    expect(add.request.body).toEqual({ count: 2, difficulty: 'easy', topic: null });

    service.removeDrawRule('exam-1', 's1', 'r1').subscribe();
    const remove = httpMock.expectOne(`${environment.apiBaseUrl}/v1/exams/exam-1/sections/s1/draw-rules/r1`);
    expect(remove.request.method).toBe('DELETE');
  });

  it('changes the name and description with a PUT to the details route', () => {
    service.updateDetails('exam-1', { name: 'Maths mock', description: null }).subscribe();

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/v1/exams/exam-1/details`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ name: 'Maths mock', description: null });
  });

  it('edits a section with a PUT that carries both its name and its time limit', () => {
    service.editSection('exam-1', 's1', { name: 'Geometry', timeSeconds: 900 }).subscribe();

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/v1/exams/exam-1/sections/s1`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ name: 'Geometry', timeSeconds: 900 });
  });

  it('removes a section, a question from a section, and a whole exam with DELETEs to their own routes', () => {
    service.removeSection('exam-1', 's1').subscribe();
    const section = httpMock.expectOne(`${environment.apiBaseUrl}/v1/exams/exam-1/sections/s1`);
    expect(section.request.method).toBe('DELETE');

    service.removeQuestion('exam-1', 's1', 'q1').subscribe();
    const question = httpMock.expectOne(`${environment.apiBaseUrl}/v1/exams/exam-1/sections/s1/questions/q1`);
    expect(question.request.method).toBe('DELETE');

    service.deleteExam('exam-1').subscribe();
    const exam = httpMock.expectOne(`${environment.apiBaseUrl}/v1/exams/exam-1`);
    expect(exam.request.method).toBe('DELETE');
  });

  it('publishes an exam with a POST to its publish route', () => {
    service.publish('exam-1').subscribe();

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/v1/exams/exam-1/publish`);
    expect(req.request.method).toBe('POST');
  });
});
