import { AttemptSummaryDto, MyExamDto } from '../candidate.models';
import { groupExams, groupOf } from './exam-groups';

const attempt = (id: string, status: 'InProgress' | 'Submitted', at: string): AttemptSummaryDto => ({
  id, number: 1, status, startedAtUtc: at, submittedAtUtc: status === 'Submitted' ? at : null, autoSubmitted: false, score: status === 'Submitted' ? 10 : null, maxScore: status === 'Submitted' ? 20 : null,
});

const exam = (examId: string, overrides: Partial<MyExamDto> = {}): MyExamDto => ({
  examId, name: examId, description: null, startUtc: '2026-10-05T04:30:00Z', endUtc: '2026-10-05T07:30:00Z', lateEntryDeadlineUtc: null, durationSeconds: 3600,
  questionCount: 10, state: 'Open', attemptId: null, attemptStatus: null, score: null, maxScore: null, attemptsAllowed: 1, attemptsUsed: 0, canStartAttempt: true,
  attempts: [], canRequestAttempt: false, attemptRequest: null, rules: null, ...overrides,
});

const ids = (exams: MyExamDto[]) => exams.map((e) => e.examId);

describe('groupExams', () => {
  it('has nothing in any group for no exams', () => {
    expect(groupExams([])).toEqual({ available: [], upcoming: [], attempted: [] });
  });

  it('puts an exam the candidate can start, resume or ask for another attempt at in the available group', () => {
    const groups = groupExams([
      exam('start'),
      exam('resume', { canStartAttempt: false, attempts: [attempt('a1', 'InProgress', '2026-10-05T05:00:00Z')] }),
      exam('ask', { canStartAttempt: false, canRequestAttempt: true, attempts: [attempt('a2', 'Submitted', '2026-10-05T05:00:00Z')] }),
      exam('waiting', { canStartAttempt: false, attempts: [attempt('a3', 'Submitted', '2026-10-05T05:00:00Z')], attemptRequest: { id: 'r', status: 'Pending', message: null, requestedAtUtc: '2026-10-05T06:00:00Z', decidedAtUtc: null, decisionNote: null } }),
    ]);

    expect(ids(groups.available).sort()).toEqual(['ask', 'resume', 'start', 'waiting']);
    expect(groups.upcoming).toEqual([]);
    expect(groups.attempted).toEqual([]);
  });

  it('keeps an open exam that was never attempted among the available ones even when it cannot be started yet, because its card says why', () => {
    const groups = groupExams([exam('locked', { canStartAttempt: false })]);

    expect(ids(groups.available)).toEqual(['locked']);
  });

  it('puts an exam that has not opened and has no attempt in the upcoming group, the one opening first at the top when ordered soonest first', () => {
    const groups = groupExams(
      [
        exam('later', { state: 'NotOpen', canStartAttempt: false, startUtc: '2026-10-20T04:30:00Z' }),
        exam('sooner', { state: 'NotOpen', canStartAttempt: false, startUtc: '2026-10-10T04:30:00Z' }),
      ],
      'soonest',
    );

    expect(ids(groups.upcoming)).toEqual(['sooner', 'later']);
  });

  it('puts exams already attempted, and exams that closed unattempted, in the attempted group, the most recent first when ordered soonest first', () => {
    const groups = groupExams(
      [
        exam('older', { state: 'Closed', canStartAttempt: false, attempts: [attempt('a1', 'Submitted', '2026-09-01T05:00:00Z')] }),
        exam('missed', { state: 'Closed', canStartAttempt: false, endUtc: '2026-09-15T07:30:00Z' }),
        exam('newer', { state: 'Closed', canStartAttempt: false, attempts: [attempt('a2', 'Submitted', '2026-10-01T05:00:00Z')] }),
      ],
      'soonest',
    );

    expect(ids(groups.attempted)).toEqual(['newer', 'missed', 'older']);
  });

  it('judges an exam by its latest attempt when it has several', () => {
    const groups = groupExams(
      [
        exam('two', { state: 'Closed', canStartAttempt: false, attempts: [attempt('a1', 'Submitted', '2026-08-01T05:00:00Z'), attempt('a2', 'Submitted', '2026-10-02T05:00:00Z')] }),
        exam('one', { state: 'Closed', canStartAttempt: false, attempts: [attempt('a3', 'Submitted', '2026-10-01T05:00:00Z')] }),
      ],
      'soonest',
    );

    expect(ids(groups.attempted)).toEqual(['two', 'one']);
  });

  it('puts an exam in progress first in the available group, then the one that closes soonest, when ordered soonest first', () => {
    const groups = groupExams(
      [
        exam('late', { endUtc: '2026-10-09T07:30:00Z' }),
        exam('soon', { endUtc: '2026-10-06T07:30:00Z' }),
        exam('busy', { endUtc: '2026-10-20T07:30:00Z', canStartAttempt: false, attempts: [attempt('a1', 'InProgress', '2026-10-05T05:00:00Z')] }),
      ],
      'soonest',
    );

    expect(ids(groups.available)).toEqual(['busy', 'soon', 'late']);
  });

  describe('newest first, which is how the page opens', () => {
    it('puts the exam made most recently first in every group, whatever order the API lists them in', () => {
      const groups = groupExams([
        exam('old', { createdAtUtc: '2026-08-01T05:00:00Z' }),
        exam('new', { createdAtUtc: '2026-10-01T05:00:00Z' }),
        exam('mid', { createdAtUtc: '2026-09-01T05:00:00Z' }),
        exam('later-old', { state: 'NotOpen', canStartAttempt: false, createdAtUtc: '2026-07-01T05:00:00Z' }),
        exam('later-new', { state: 'NotOpen', canStartAttempt: false, createdAtUtc: '2026-10-02T05:00:00Z' }),
        exam('done-old', { state: 'Closed', canStartAttempt: false, createdAtUtc: '2026-06-01T05:00:00Z' }),
        exam('done-new', { state: 'Closed', canStartAttempt: false, createdAtUtc: '2026-09-15T05:00:00Z' }),
      ]);

      expect(ids(groups.available)).toEqual(['new', 'mid', 'old']);
      expect(ids(groups.upcoming)).toEqual(['later-new', 'later-old']);
      expect(ids(groups.attempted)).toEqual(['done-new', 'done-old']);
    });

    it('is what groupExams does when no order is asked for', () => {
      const input = [exam('old', { createdAtUtc: '2026-08-01T05:00:00Z' }), exam('new', { createdAtUtc: '2026-10-01T05:00:00Z' })];

      expect(groupExams(input)).toEqual(groupExams(input, 'newest'));
      expect(ids(groupExams(input).available)).toEqual(['new', 'old']);
    });

    it('does not move an exam up because someone edited it: only when it was made counts', () => {
      // The API sends the day the exam was made, not the day it was last changed, so this is what the page has to go on.
      const groups = groupExams([
        exam('made-first', { createdAtUtc: '2026-08-01T05:00:00Z', endUtc: '2026-10-30T07:30:00Z' }),
        exam('made-last', { createdAtUtc: '2026-10-01T05:00:00Z', endUtc: '2026-10-05T07:30:00Z' }),
      ]);

      expect(ids(groups.available)).toEqual(['made-last', 'made-first']);
    });

    it('keeps an exam in progress above a newer exam, so carrying on with it is never a search', () => {
      const groups = groupExams([
        exam('newest', { createdAtUtc: '2026-10-01T05:00:00Z' }),
        exam('busy', { createdAtUtc: '2026-07-01T05:00:00Z', canStartAttempt: false, attempts: [attempt('a1', 'InProgress', '2026-10-05T05:00:00Z')] }),
      ]);

      expect(ids(groups.available)).toEqual(['busy', 'newest']);
    });

    it('puts exams made at the same moment, or from an API that gives no date, by the later window first, then by name', () => {
      const groups = groupExams([
        exam('b', { startUtc: '2026-10-05T04:30:00Z' }),
        exam('c', { startUtc: '2026-10-09T04:30:00Z' }),
        exam('a', { startUtc: '2026-10-05T04:30:00Z' }),
        exam('d', { startUtc: '2026-10-05T04:30:00Z', createdAtUtc: null }),
      ]);

      expect(ids(groups.available)).toEqual(['c', 'a', 'b', 'd']);
    });

    it('reads a date it cannot understand as no date, rather than putting the exam anywhere odd', () => {
      const groups = groupExams([exam('broken', { createdAtUtc: 'not a date' }), exam('real', { createdAtUtc: '2026-10-01T05:00:00Z' })]);

      expect(ids(groups.available)).toEqual(['real', 'broken']);
    });
  });

  describe('by name', () => {
    it('puts the exams in alphabetical order inside each group, ignoring case and reading numbers as numbers', () => {
      const groups = groupExams([exam('Test 10'), exam('test 2'), exam('Algebra'), exam('zebra')], 'name');

      expect(ids(groups.available)).toEqual(['Algebra', 'test 2', 'Test 10', 'zebra']);
    });

    it('still keeps an exam in progress first', () => {
      const groups = groupExams(
        [exam('Algebra'), exam('Zoology', { canStartAttempt: false, attempts: [attempt('a1', 'InProgress', '2026-10-05T05:00:00Z')] })],
        'name',
      );

      expect(ids(groups.available)).toEqual(['Zoology', 'Algebra']);
    });
  });

  describe('groupOf', () => {
    it('says which group an exam is in, the same one groupExams puts it in', () => {
      const take = exam('take');
      const coming = exam('coming', { state: 'NotOpen', canStartAttempt: false });
      const done = exam('done', { state: 'Closed', canStartAttempt: false });

      expect([take, coming, done].map(groupOf)).toEqual(['available', 'upcoming', 'attempted']);
      for (const item of [take, coming, done]) {
        expect(groupExams([item])[groupOf(item)]).toEqual([item]);
      }
    });
  });

  it('puts every exam in exactly one group and does not change what it was given', () => {
    const input = [exam('a'), exam('b', { state: 'NotOpen', canStartAttempt: false }), exam('c', { state: 'Closed', canStartAttempt: false })];
    const before = ids(input);

    const groups = groupExams(input);

    expect(ids(input)).toEqual(before);
    expect([...groups.available, ...groups.upcoming, ...groups.attempted].map((e) => e.examId).sort()).toEqual(['a', 'b', 'c']);
  });
});
