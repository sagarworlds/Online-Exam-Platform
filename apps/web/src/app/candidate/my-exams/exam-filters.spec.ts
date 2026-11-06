import { AttemptSummaryDto, MyExamDto } from '../candidate.models';
import { CLOSING_SOON_MS, countByFilter, matchesFilter, matchesQuery } from './exam-filters';

const NOW = Date.parse('2026-10-05T10:00:00Z');
const inHours = (hours: number) => new Date(NOW + hours * 3_600_000).toISOString();

const attempt = (status: 'InProgress' | 'Submitted'): AttemptSummaryDto => ({
  id: 'a1', number: 1, status, startedAtUtc: inHours(-3), submittedAtUtc: status === 'Submitted' ? inHours(-2) : null, autoSubmitted: false,
  score: status === 'Submitted' ? 10 : null, maxScore: status === 'Submitted' ? 20 : null,
});

const exam = (examId: string, overrides: Partial<MyExamDto> = {}): MyExamDto => ({
  examId, name: examId, description: null, startUtc: inHours(-5), endUtc: inHours(100), lateEntryDeadlineUtc: null, durationSeconds: 3600,
  questionCount: 10, state: 'Open', attemptId: null, attemptStatus: null, score: null, maxScore: null, attemptsAllowed: 1, attemptsUsed: 0, canStartAttempt: true,
  attempts: [], canRequestAttempt: false, attemptRequest: null, rules: null, ...overrides,
});

describe('matchesQuery', () => {
  it('lets every exam through when nothing, or only spaces, was typed', () => {
    expect(matchesQuery(exam('Maths'), '')).toBe(true);
    expect(matchesQuery(exam('Maths'), '   ')).toBe(true);
  });

  it('finds part of a name in any case', () => {
    expect(matchesQuery(exam('Class 4 Maths Final'), 'maths')).toBe(true);
    expect(matchesQuery(exam('Class 4 Maths Final'), 'FIN')).toBe(true);
    expect(matchesQuery(exam('Class 4 Maths Final'), 'science')).toBe(false);
  });

  it('wants every word typed, in any order, so a few words narrow the list instead of widening it', () => {
    const maths = exam('Final exam: Maths');

    expect(matchesQuery(maths, 'maths final')).toBe(true);
    expect(matchesQuery(maths, '  final   maths ')).toBe(true);
    expect(matchesQuery(maths, 'maths science')).toBe(false);
  });

  it('looks in the description too, which is where an exam says its subject when its name does not', () => {
    const exam1 = exam('Unit test 3', { description: 'Fractions and decimals, class 4 mathematics' });

    expect(matchesQuery(exam1, 'mathematics')).toBe(true);
    expect(matchesQuery(exam1, 'unit mathematics')).toBe(true);
    expect(matchesQuery(exam('Unit test 3'), 'mathematics')).toBe(false);
  });

  it('does not read what was typed as a pattern', () => {
    expect(matchesQuery(exam('Maths (final)'), '(final')).toBe(true);
    expect(matchesQuery(exam('Maths'), '.*')).toBe(false);
  });
});

describe('matchesFilter', () => {
  it('lets every exam through "all"', () => {
    for (const item of [exam('a'), exam('b', { state: 'Closed', canStartAttempt: false }), exam('c', { state: 'NotOpen', canStartAttempt: false })]) {
      expect(matchesFilter(item, 'all', NOW)).toBe(true);
    }
  });

  it('shows the exam the candidate started and has not finished under "in progress", and no other', () => {
    const busy = exam('busy', { canStartAttempt: false, attempts: [attempt('InProgress')] });
    const finished = exam('finished', { state: 'Closed', canStartAttempt: false, attempts: [attempt('Submitted')] });

    expect(matchesFilter(busy, 'inProgress', NOW)).toBe(true);
    expect(matchesFilter(finished, 'inProgress', NOW)).toBe(false);
    expect(matchesFilter(exam('fresh'), 'inProgress', NOW)).toBe(false);
  });

  describe('closing soon', () => {
    it('is an open exam the candidate can still act on that closes within a day', () => {
      expect(matchesFilter(exam('soon', { endUtc: inHours(3) }), 'closingSoon', NOW)).toBe(true);
      expect(matchesFilter(exam('later', { endUtc: inHours(60) }), 'closingSoon', NOW)).toBe(false);
    });

    it('includes an exam that closes exactly a day from now, and not one a minute after', () => {
      expect(matchesFilter(exam('edge', { endUtc: new Date(NOW + CLOSING_SOON_MS).toISOString() }), 'closingSoon', NOW)).toBe(true);
      expect(matchesFilter(exam('over', { endUtc: new Date(NOW + CLOSING_SOON_MS + 60_000).toISOString() }), 'closingSoon', NOW)).toBe(false);
    });

    it('includes an exam in progress that is about to close, because that is the one to hurry back to', () => {
      const busy = exam('busy', { endUtc: inHours(2), canStartAttempt: false, attempts: [attempt('InProgress')] });

      expect(matchesFilter(busy, 'closingSoon', NOW)).toBe(true);
    });

    it('leaves out an exam whose end has passed on this device, an exam not yet open, and one already done with', () => {
      expect(matchesFilter(exam('passed', { endUtc: inHours(-1) }), 'closingSoon', NOW)).toBe(false);
      expect(matchesFilter(exam('notYet', { state: 'NotOpen', canStartAttempt: false, startUtc: inHours(1), endUtc: inHours(5) }), 'closingSoon', NOW)).toBe(false);
      expect(matchesFilter(exam('done', { endUtc: inHours(3), canStartAttempt: false, attempts: [attempt('Submitted')] }), 'closingSoon', NOW)).toBe(false);
    });
  });

  it('shows an exam that has not opened under "not open yet"', () => {
    expect(matchesFilter(exam('later', { state: 'NotOpen', canStartAttempt: false }), 'notOpen', NOW)).toBe(true);
    expect(matchesFilter(exam('now'), 'notOpen', NOW)).toBe(false);
  });

  it('shows what the page keeps under its attempted heading under "done": the same exams', () => {
    const finished = exam('finished', { state: 'Closed', canStartAttempt: false, attempts: [attempt('Submitted')] });
    const missed = exam('missed', { state: 'Closed', canStartAttempt: false });

    expect(matchesFilter(finished, 'done', NOW)).toBe(true);
    expect(matchesFilter(missed, 'done', NOW)).toBe(true);
    expect(matchesFilter(exam('fresh'), 'done', NOW)).toBe(false);
    expect(matchesFilter(exam('later', { state: 'NotOpen', canStartAttempt: false }), 'done', NOW)).toBe(false);
  });
});

describe('countByFilter', () => {
  it('says how many exams each filter would show', () => {
    const exams = [
      exam('soon', { endUtc: inHours(3) }),
      exam('later'),
      exam('busy', { endUtc: inHours(2), canStartAttempt: false, attempts: [attempt('InProgress')] }),
      exam('coming', { state: 'NotOpen', canStartAttempt: false }),
      exam('finished', { state: 'Closed', canStartAttempt: false, attempts: [attempt('Submitted')] }),
      exam('missed', { state: 'Closed', canStartAttempt: false }),
    ];

    expect(countByFilter(exams, NOW)).toEqual({ all: 6, inProgress: 1, closingSoon: 2, notOpen: 1, done: 2 });
  });

  it('counts nothing for no exams', () => {
    expect(countByFilter([], NOW)).toEqual({ all: 0, inProgress: 0, closingSoon: 0, notOpen: 0, done: 0 });
  });
});
