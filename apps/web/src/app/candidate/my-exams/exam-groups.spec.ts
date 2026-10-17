import { AttemptSummaryDto, MyExamDto } from '../candidate.models';
import { groupExams } from './exam-groups';

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

  it('puts an exam that has not opened and has no attempt in the upcoming group, the one opening first at the top', () => {
    const groups = groupExams([
      exam('later', { state: 'NotOpen', canStartAttempt: false, startUtc: '2026-10-20T04:30:00Z' }),
      exam('sooner', { state: 'NotOpen', canStartAttempt: false, startUtc: '2026-10-10T04:30:00Z' }),
    ]);

    expect(ids(groups.upcoming)).toEqual(['sooner', 'later']);
  });

  it('puts exams already attempted, and exams that closed unattempted, in the attempted group, the most recent first', () => {
    const groups = groupExams([
      exam('older', { state: 'Closed', canStartAttempt: false, attempts: [attempt('a1', 'Submitted', '2026-09-01T05:00:00Z')] }),
      exam('missed', { state: 'Closed', canStartAttempt: false, endUtc: '2026-09-15T07:30:00Z' }),
      exam('newer', { state: 'Closed', canStartAttempt: false, attempts: [attempt('a2', 'Submitted', '2026-10-01T05:00:00Z')] }),
    ]);

    expect(ids(groups.attempted)).toEqual(['newer', 'missed', 'older']);
  });

  it('judges an exam by its latest attempt when it has several', () => {
    const groups = groupExams([
      exam('two', { state: 'Closed', canStartAttempt: false, attempts: [attempt('a1', 'Submitted', '2026-08-01T05:00:00Z'), attempt('a2', 'Submitted', '2026-10-02T05:00:00Z')] }),
      exam('one', { state: 'Closed', canStartAttempt: false, attempts: [attempt('a3', 'Submitted', '2026-10-01T05:00:00Z')] }),
    ]);

    expect(ids(groups.attempted)).toEqual(['two', 'one']);
  });

  it('puts an exam in progress first in the available group, then the one that closes soonest', () => {
    const groups = groupExams([
      exam('late', { endUtc: '2026-10-09T07:30:00Z' }),
      exam('soon', { endUtc: '2026-10-06T07:30:00Z' }),
      exam('busy', { endUtc: '2026-10-20T07:30:00Z', canStartAttempt: false, attempts: [attempt('a1', 'InProgress', '2026-10-05T05:00:00Z')] }),
    ]);

    expect(ids(groups.available)).toEqual(['busy', 'soon', 'late']);
  });

  it('puts every exam in exactly one group and does not change what it was given', () => {
    const input = [exam('a'), exam('b', { state: 'NotOpen', canStartAttempt: false }), exam('c', { state: 'Closed', canStartAttempt: false })];
    const before = ids(input);

    const groups = groupExams(input);

    expect(ids(input)).toEqual(before);
    expect([...groups.available, ...groups.upcoming, ...groups.attempted].map((e) => e.examId).sort()).toEqual(['a', 'b', 'c']);
  });
});
