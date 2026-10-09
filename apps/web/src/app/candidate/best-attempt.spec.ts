import { bestAttemptId } from './best-attempt';
import { AttemptSummaryDto } from './candidate.models';

const attempt = (number: number, score: number | null, status: AttemptSummaryDto['status'] = 'Submitted'): AttemptSummaryDto => ({
  id: `a${number}`,
  number,
  status,
  startedAtUtc: '2026-10-05T04:30:00Z',
  submittedAtUtc: status === 'Submitted' ? '2026-10-05T04:50:00Z' : null,
  autoSubmitted: false,
  score,
  maxScore: 10,
});

describe('bestAttemptId', () => {
  it('picks the submitted attempt with the highest score', () => {
    expect(bestAttemptId([attempt(1, 4), attempt(2, 9), attempt(3, 6)])).toBe('a2');
  });

  it('prefers the earlier attempt when two tie', () => {
    expect(bestAttemptId([attempt(1, 7), attempt(2, 7)])).toBe('a1');
  });

  it('says nothing when fewer than two attempts are submitted, since the only result is not "best"', () => {
    expect(bestAttemptId([])).toBeNull();
    expect(bestAttemptId([attempt(1, 9)])).toBeNull();
    expect(bestAttemptId([attempt(1, 9), attempt(2, null, 'InProgress')])).toBeNull();
  });

  it('ignores an attempt still in progress, and compares negative scores correctly', () => {
    expect(bestAttemptId([attempt(1, -2), attempt(2, -1), attempt(3, null, 'InProgress')])).toBe('a2');
  });

  it('does not depend on the order it is given in', () => {
    expect(bestAttemptId([attempt(2, 5), attempt(1, 5)])).toBe('a1');
  });
});
