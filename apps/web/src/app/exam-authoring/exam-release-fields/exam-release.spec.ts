import { ExamConfigDto } from '../exam.models';
import { INSTANT_RELEASE, isReleaseComplete, selectionOfRelease, toReleaseRequest } from './exam-release';

const config = (overrides: Partial<ExamConfigDto>): ExamConfigDto => ({
  totalTimeSeconds: null,
  shuffleQuestions: true,
  shuffleOptions: true,
  sectionLockEnabled: false,
  calculatorAllowed: false,
  scratchpadAllowed: true,
  maxAttempts: 1,
  maxRetakes: 0,
  resultReleaseMode: 'Instant',
  resultReleaseTime: null,
  markingScheme: { correctMarks: 1, incorrectMarks: 0, unattemptedMarks: 0 },
  ...overrides,
});

describe('exam release choice', () => {
  it('starts as "right after submitting", which is what every exam did before', () => {
    expect(INSTANT_RELEASE.mode).toBe('Instant');
    expect(isReleaseComplete(INSTANT_RELEASE)).toBe(true);
  });

  it('needs a time that reads as a date for a scheduled release, and nothing for the others', () => {
    expect(isReleaseComplete({ mode: 'Scheduled', localTime: '' })).toBe(false);
    expect(isReleaseComplete({ mode: 'Scheduled', localTime: 'not a date' })).toBe(false);
    expect(isReleaseComplete({ mode: 'Scheduled', localTime: '2026-10-08T14:30' })).toBe(true);
    expect(isReleaseComplete({ mode: 'Manual', localTime: '' })).toBe(true);
  });

  it('sends a scheduled time as a UTC instant, and no time at all for the other modes', () => {
    const local = '2026-10-08T14:30';

    expect(toReleaseRequest({ mode: 'Scheduled', localTime: local })).toEqual({ mode: 'Scheduled', releaseTime: new Date(local).toISOString() });
    // A stale time left in the form must never travel with Instant or Manual.
    expect(toReleaseRequest({ mode: 'Manual', localTime: local })).toEqual({ mode: 'Manual', releaseTime: null });
    expect(toReleaseRequest({ mode: 'Instant', localTime: local })).toEqual({ mode: 'Instant', releaseTime: null });
  });

  it('starts the form from what the exam is set to, turning a stored UTC time into the browser’s local time', () => {
    const utc = '2026-10-08T09:00:00.000Z';

    const selection = selectionOfRelease(config({ resultReleaseMode: 'Scheduled', resultReleaseTime: utc }));

    expect(selection.mode).toBe('Scheduled');
    // Whatever the machine's zone, reading the local text back gives the same instant.
    expect(new Date(selection.localTime).toISOString()).toBe(utc);
    expect(selectionOfRelease(config({ resultReleaseMode: 'Manual' }))).toEqual({ mode: 'Manual', localTime: '' });
    expect(selectionOfRelease(undefined)).toEqual({ mode: 'Instant', localTime: '' });
  });
});
