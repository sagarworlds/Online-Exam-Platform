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

  it('keeps the first three waiting items of each queue, for the lines under a tile', () => {
    const service = create(['exam.manage']);

    service.refresh();
    answerQueues({
      requests: ['a', 'b', 'c', 'd'].map((id) => ({ id, candidateEmail: `${id}@example.com`, examName: 'Maths' })),
      disputes: [{ id: 'x', candidateEmail: null, examName: 'Physics' }],
    });

    expect(service.latest().attemptRequests?.map((item) => item.title)).toEqual(['a@example.com', 'b@example.com', 'c@example.com']);
    expect(service.latest().attemptRequests?.[0]).toEqual({ id: 'a', title: 'a@example.com', subtitle: 'Maths', path: '/admin/attempt-requests' });
    expect(service.latest().disputes).toEqual([{ id: 'x', title: 'Physics', subtitle: null, path: '/admin/disputes' }]);
  });

  it('counts the open risk flags of every exam from one read, exactly, for a reviewer only', () => {
    const service = create(['proctoring.review']);

    service.refresh();
    const read = http.expectOne(isRead('/v1/proctoring/risk-flags/open'));
    expect(read.request.params.get('limit')).toBe('3');
    read.flush({ total: 250, items: [{ id: 'f1', examId: 'e1', examName: null, attemptNumber: 1, score: 40, maxScore: 100, computedAtUtc: '2026-10-01T05:00:00Z' }] });

    expect(service.counts().riskFlags).toEqual({ status: 'ready', count: 250, atLimit: false });
    expect(service.latest().riskFlags).toEqual([{ id: 'f1', title: 'Exam no longer available', subtitle: '40 / 100', path: '/exams/e1/risk-flags' }]);
  });

  it('drops the lines of what was dealt with, and forgets them all on sign-out', () => {
    const service = create(['exam.manage']);
    service.refresh();
    answerQueues({ requests: ['a', 'b'].map((id) => ({ id, candidateEmail: id, examName: null })) });

    service.settled('attemptRequests');
    expect(service.latest().attemptRequests).toBeUndefined();

    service.clear();
    expect(service.latest()).toEqual({});
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

  describe('when an item is dealt with on its queue’s own page', () => {
    it('drops the count at once, never below none, and reads again on the next page change however recent the last read was', () => {
      vi.useFakeTimers();
      const service = create(['exam.manage']);
      service.refresh();
      answerQueues({ requests: [{}, {}], disputes: [{}] });

      service.settled('attemptRequests');
      expect(service.counts().attemptRequests).toEqual({ status: 'ready', count: 1, atLimit: false });
      service.settled('disputes', 5);
      expect(service.counts().disputes).toEqual({ status: 'ready', count: 0, atLimit: false });

      service.refreshIfStale();
      answerQueues();
    });

    it('leaves a count that stopped at the list’s limit alone, since it was never exact', () => {
      const service = create(['exam.manage']);
      service.refresh();
      answerQueues({ requests: Array.from({ length: 200 }, () => ({})) });

      service.settled('attemptRequests');

      expect(service.counts().attemptRequests).toEqual({ status: 'ready', count: 200, atLimit: true });
    });

    it('does nothing for a queue that was not read', () => {
      const service = create(['exam.manage']);

      service.settled('disputes');

      expect(service.counts()).toEqual({});
    });

    it('says a full exam list may hide running exams beyond it', () => {
      const service = create(['exam.read']);
      const exam = () => ({ status: 'Published', isScheduled: true, scheduledStartTime: new Date(Date.now() - hour).toISOString(), scheduledEndTime: new Date(Date.now() + hour).toISOString() });

      service.refresh();
      http.expectOne(isRead('/v1/exams')).flush(Array.from({ length: 200 }, exam));

      expect(service.counts().liveExams).toEqual({ status: 'ready', count: 200, atLimit: true });
    });
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
