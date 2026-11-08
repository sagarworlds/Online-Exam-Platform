import { TestBed } from '@angular/core/testing';
import { AttemptDto } from '../candidate.models';
import { AttemptHandoff, HANDOFF_MAX_AGE_MS } from './attempt-handoff';

describe('AttemptHandoff', () => {
  const NOW = Date.parse('2026-10-05T04:30:00Z');
  let handoff: AttemptHandoff;

  const attempt = (overrides: Partial<AttemptDto> = {}): AttemptDto => ({
    id: 'a1',
    examId: 'e1',
    examName: 'Maths Final',
    status: 'InProgress',
    startedAtUtc: '2026-10-05T04:30:00Z',
    deadlineUtc: '2026-10-05T05:00:00Z',
    submittedAtUtc: null,
    autoSubmitted: false,
    score: null,
    maxScore: null,
    serverTimeUtc: '2026-10-05T04:30:00Z',
    sections: [],
    ...overrides,
  });

  beforeEach(() => {
    handoff = TestBed.inject(AttemptHandoff);
  });

  it('gives the attempt that was started, with the moment it arrived', () => {
    const started = attempt();
    handoff.put(started, false, NOW);

    expect(handoff.take('a1', false, NOW + 1_000)).toEqual({ attempt: started, receivedAt: NOW });
  });

  it('gives it once: a second page, or a reload, reads the server', () => {
    handoff.put(attempt(), false, NOW);

    expect(handoff.take('a1', false, NOW)).not.toBeNull();
    expect(handoff.take('a1', false, NOW)).toBeNull();
  });

  it('gives nothing when nothing was handed over', () => {
    expect(handoff.take('a1', false, NOW)).toBeNull();
  });

  it('gives nothing for another attempt, and forgets what it held', () => {
    handoff.put(attempt(), false, NOW);

    expect(handoff.take('someone-elses', false, NOW)).toBeNull();
    expect(handoff.take('a1', false, NOW)).toBeNull();
  });

  it('gives nothing for an attempt that is not open, since the server decides what a finished one shows', () => {
    handoff.put(attempt({ status: 'Submitted' }), false, NOW);

    expect(handoff.take('a1', false, NOW)).toBeNull();
  });

  it('gives nothing when the page is in the other bandwidth mode than the start was asked in', () => {
    handoff.put(attempt(), true, NOW);

    expect(handoff.take('a1', false, NOW)).toBeNull();
  });

  it('gives it when the modes agree, in either mode', () => {
    handoff.put(attempt(), true, NOW);

    expect(handoff.take('a1', true, NOW)?.attempt.id).toBe('a1');
  });

  it('gives it up to the age limit and not past it, since the clock reading that came with it is the start of the countdown', () => {
    handoff.put(attempt(), false, NOW);
    expect(handoff.take('a1', false, NOW + HANDOFF_MAX_AGE_MS)).not.toBeNull();

    handoff.put(attempt(), false, NOW);
    expect(handoff.take('a1', false, NOW + HANDOFF_MAX_AGE_MS + 1)).toBeNull();
  });

  it('gives nothing when the device clock appears to have gone backwards since it arrived', () => {
    handoff.put(attempt(), false, NOW);

    expect(handoff.take('a1', false, NOW - 1)).toBeNull();
  });

  it('holds only the latest attempt started', () => {
    handoff.put(attempt({ id: 'a1' }), false, NOW);
    handoff.put(attempt({ id: 'a2' }), false, NOW);

    expect(handoff.take('a1', false, NOW)).toBeNull();
  });
});
