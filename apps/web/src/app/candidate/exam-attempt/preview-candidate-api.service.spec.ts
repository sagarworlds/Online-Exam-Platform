import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { PreviewCandidateApiService } from './preview-candidate-api.service';

describe('PreviewCandidateApiService', () => {
  let service: PreviewCandidateApiService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), PreviewCandidateApiService] });
    service = TestBed.inject(PreviewCandidateApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('reads the exam from the staff preview endpoint, by the exam id', () => {
    let body: unknown;
    service.getAttempt('exam-1').subscribe((a) => (body = a));

    const request = httpMock.expectOne((r) => r.method === 'GET' && r.url.endsWith('/v1/exams/exam-1/preview'));
    request.flush({ examName: 'Maths' });

    expect(body).toEqual({ examName: 'Maths' });
  });

  it('accepts and drops everything the page would save, sending nothing', () => {
    const done: string[] = [];
    service.saveAnswer().subscribe(() => done.push('answer'));
    service.saveAnswers().subscribe(() => done.push('answers'));
    service.clearAnswer().subscribe(() => done.push('clear'));
    service.markForReview().subscribe(() => done.push('mark'));
    service.unmarkForReview().subscribe(() => done.push('unmark'));
    service.moveToSection().subscribe(() => done.push('section'));

    expect(done).toEqual(['answer', 'answers', 'clear', 'mark', 'unmark', 'section']);
  });

  it('never reports, checks or submits anything', () => {
    let emitted = false;
    service.getAttemptStatus().subscribe(() => (emitted = true));
    service.reportFocusViolation().subscribe(() => (emitted = true));
    service.submitAttempt().subscribe(() => (emitted = true));

    expect(emitted).toBe(false);
  });
});
