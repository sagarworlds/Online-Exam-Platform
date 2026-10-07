import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../environments/environment';
import { QuestionApiService } from './question-api.service';
import { AnswerKeyCorrectionResult } from './question.models';

describe('QuestionApiService', () => {
  let service: QuestionApiService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(QuestionApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  describe('correctAnswerKey', () => {
    it('posts the correct options and the reason to the question’s correct-answer-key route and returns what it did', () => {
      let result: AnswerKeyCorrectionResult | undefined;

      service.correctAnswerKey('q1', ['o2', 'o3'], 'The key marked Rome').subscribe((r) => (result = r));

      const request = httpMock.expectOne(`${environment.apiBaseUrl}/v1/questions/q1/correct-answer-key`);
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({ correctOptionIds: ['o2', 'o3'], reason: 'The key marked Rome' });
      request.flush({ keyChanged: true, attemptsRescored: 4 });
      expect(result).toEqual({ keyChanged: true, attemptsRescored: 4 });
    });

    it('returns a correction that changed nothing as it is', () => {
      let result: AnswerKeyCorrectionResult | undefined;

      service.correctAnswerKey('q1', ['o1'], 'Checked again').subscribe((r) => (result = r));

      httpMock.expectOne(`${environment.apiBaseUrl}/v1/questions/q1/correct-answer-key`).flush({ keyChanged: false, attemptsRescored: 0 });
      expect(result).toEqual({ keyChanged: false, attemptsRescored: 0 });
    });

    it('passes the API’s refusal on to the caller', () => {
      let status: number | undefined;

      service.correctAnswerKey('q1', [], 'Why').subscribe({ error: (error: { status: number }) => (status = error.status) });

      httpMock.expectOne(`${environment.apiBaseUrl}/v1/questions/q1/correct-answer-key`).flush({ title: 'invalid_question' }, { status: 400, statusText: 'Bad Request' });
      expect(status).toBe(400);
    });
  });
});
