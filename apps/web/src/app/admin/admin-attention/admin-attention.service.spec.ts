import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { AuthSessionService } from '../../auth/auth-session.service';
import { AdminAttentionService } from './admin-attention.service';

describe('AdminAttentionService', () => {
  let http: HttpTestingController;
  let permissions: string[];

  const isRead = (path: string) => (r: { method: string; url: string }) => r.method === 'GET' && r.url.includes(path);
  const hour = 3_600_000;

  function create(held: string[]): AdminAttentionService {
    permissions = held;
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthSessionService, useValue: { hasPermission: (code: string) => permissions.includes(code) } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    return TestBed.inject(AdminAttentionService);
  }

  function answerQueues(answers: { requests?: unknown[]; disputes?: unknown[]; issues?: unknown[] } = {}): void {
    http.expectOne(isRead('/v1/attempt-requests')).flush(answers.requests ?? []);
    http.expectOne(isRead('/v1/disputes')).flush(answers.disputes ?? []);
    http.expectOne(isRead('/v1/issue-reports')).flush(answers.issues ?? []);
  }

  afterEach(() => {
    http.verify();
    vi.useRealTimers();
  });

  it('asks for the waiting items of each queue, which is how the API names the ones that need a decision', () => {
    const service = create(['exam.manage']);

    service.refresh();

    expect(http.expectOne(isRead('/v1/attempt-requests')).request.params.get('status')).toBe('pending');
    expect(http.expectOne(isRead('/v1/disputes')).request.params.get('status')).toBe('open');
    expect(http.expectOne(isRead('/v1/issue-reports')).request.params.get('status')).toBe('open');
  });

  it('is loading until the lists arrive, then holds the length of each', () => {
    const service = create(['exam.manage']);

    service.refresh();
    expect(service.counts().disputes).toEqual({ status: 'loading' });
    expect(service.refreshing()).toBe(true);

    answerQueues({ requests: [{}, {}], disputes: [{}], issues: [] });

    expect(service.counts()).toEqual({
      attemptRequests: { status: 'ready', count: 2, atLimit: false },
      disputes: { status: 'ready', count: 1, atLimit: false },
      issues: { status: 'ready', count: 0, atLimit: false },
    });
    expect(service.refreshing()).toBe(false);
  });

  it('knows a queue may hold more than its list shows when the list is full', () => {
    const service = create(['exam.manage']);

    service.refresh();
    answerQueues({ disputes: Array.from({ length: 200 }, () => ({})) });

    expect(service.counts().disputes).toEqual({ status: 'ready', count: 200, atLimit: true });
  });

  it('counts the exams running now from the exam list: published, scheduled, started and not over', () => {
    const service = create(['exam.read']);
    const exam = (over: Record<string, unknown>) => ({
      status: 'Published',
      isScheduled: true,
      scheduledStartTime: new Date(Date.now() - hour).toISOString(),
      scheduledEndTime: new Date(Date.now() + hour).toISOString(),
      ...over,
    });

    service.refresh();
    http.expectOne(isRead('/v1/exams')).flush([exam({}), exam({ status: 'Draft' }), exam({ isScheduled: false }), exam({ scheduledEndTime: new Date(Date.now() - 1000).toISOString() })]);

    expect(service.counts()).toEqual({ liveExams: { status: 'ready', count: 1, atLimit: false } });
  });

  it('reads only what the permissions open: nothing at all for someone with no queue', () => {
    const none = create(['question.read']);

    none.refresh();

    http.expectNone(() => true);
    expect(none.counts()).toEqual({});
  });

  it('does not ask for the queues of someone who may read exams but not manage them', () => {
    const service = create(['exam.read']);

    service.refresh();

    http.expectNone(isRead('/v1/disputes'));
    http.expectOne(isRead('/v1/exams')).flush([]);
  });

  it('marks a queue that could not be read as failed, and still holds the others', () => {
    const service = create(['exam.manage']);

    service.refresh();
    http.expectOne(isRead('/v1/attempt-requests')).flush({}, { status: 500, statusText: 'Server Error' });
    http.expectOne(isRead('/v1/disputes')).flush([{}]);
    http.expectOne(isRead('/v1/issue-reports')).flush([]);

    expect(service.counts().attemptRequests).toEqual({ status: 'failed' });
    expect(service.counts().disputes).toEqual({ status: 'ready', count: 1, atLimit: false });
  });

  it('keeps the numbers it had while it reads again, and does not start a second read while one is on its way', () => {
    const service = create(['exam.manage']);
    service.refresh();
    answerQueues({ requests: [{}] });

    service.refresh();
    service.refresh();

    expect(service.counts().attemptRequests).toEqual({ status: 'ready', count: 1, atLimit: false });
    answerQueues({ requests: [{}, {}, {}] });
    expect(service.counts().attemptRequests).toEqual({ status: 'ready', count: 3, atLimit: false });
  });

  describe('moving to another page', () => {
    it('reads again once the counts are a minute old, and not before', () => {
      vi.useFakeTimers();
      const service = create(['exam.manage']);
      service.refreshIfStale();
      answerQueues();

      vi.advanceTimersByTime(59_000);
      service.refreshIfStale();
      http.expectNone(isRead('/v1/disputes'));

      vi.advanceTimersByTime(2_000);
      service.refreshIfStale();
      answerQueues({ disputes: [{}] });
      expect(service.counts().disputes).toEqual({ status: 'ready', count: 1, atLimit: false });
    });
  });

  describe('when someone signs out', () => {
    it('forgets the counts, so the next person does not see the last one’s', () => {
      const service = create(['exam.manage']);
      service.refresh();
      answerQueues({ requests: [{}] });

      service.clear();

      expect(service.counts()).toEqual({});
    });

    it('does not let a read that was on its way write its answer for the next person', () => {
      const service = create(['exam.manage']);
      service.refresh();

      service.clear();
      answerQueues({ requests: [{}, {}] });

      expect(service.counts()).toEqual({});
      expect(service.refreshing()).toBe(false);
    });
  });
});
