import { englishWords } from '../../i18n/i18n.service';
import { ExamConfigDto } from '../exam.models';
import { ExamSettingsSummary } from './exam-settings-summary';

/** A draft's settings as the API sends them, with each one at its default; a test changes only the setting it is about. */
function config(overrides: Partial<ExamConfigDto> = {}): ExamConfigDto {
  return {
    totalTimeSeconds: null,
    shuffleQuestions: false,
    shuffleOptions: false,
    maxAttempts: 1,
    resultReleaseMode: 'Instant',
    resultReleaseTime: null,
    markingScheme: {
      correctMarks: 1,
      incorrectMarks: 0,
      unattemptedMarks: 0,
      partialCredit: false,
    },
    contentProtection: true,
    focusViolationLimit: 0,
    ...overrides,
  } as ExamConfigDto;
}

describe('exam settings summary', () => {
  const summary = new ExamSettingsSummary(englishWords);

  it('says when candidates see which answers were right, in a few words', () => {
    expect(summary.release(config())).toBe('Right after they submit');
    expect(
      summary.release(
        config({ resultReleaseMode: 'Scheduled', resultReleaseTime: '2026-10-06T10:00:00Z' }),
      ),
    ).toBe('From a set time');
    expect(summary.release(config({ resultReleaseMode: 'Manual', resultReleaseTime: null }))).toBe(
      'Held back until released',
    );
    expect(
      summary.release(
        config({ resultReleaseMode: 'Manual', resultReleaseTime: '2026-10-06T10:00:00Z' }),
      ),
    ).toBe('Released');
  });

  it('names which parts are shuffled, or that the order is the written one', () => {
    expect(summary.shuffle(config())).toBe('In the written order');
    expect(summary.shuffle(config({ shuffleQuestions: true }))).toBe('Questions shuffled');
    expect(summary.shuffle(config({ shuffleOptions: true }))).toBe('Options shuffled');
    expect(summary.shuffle(config({ shuffleQuestions: true, shuffleOptions: true }))).toBe(
      'Questions and options shuffled',
    );
  });

  it('gives the three marks in the order the marking card asks for them', () => {
    const scheme = {
      correctMarks: 4,
      incorrectMarks: -1,
      unattemptedMarks: 0,
      partialCredit: false,
    };

    expect(summary.marks(config({ markingScheme: scheme }))).toBe(
      'Correct 4 · incorrect -1 · unanswered 0',
    );
  });

  it('counts attempts in words, singular for one', () => {
    expect(summary.attempts(config({ maxAttempts: 1 }))).toBe('1 attempt');
    expect(summary.attempts(config({ maxAttempts: 3 }))).toBe('3 attempts');
  });

  it('treats copying and printing as blocked unless the exam says otherwise, as the API does', () => {
    expect(summary.copying(config())).toBe('Copying and printing turned off');
    expect(summary.copying(config({ contentProtection: false }))).toBe(
      'Copying and printing allowed',
    );
    expect(summary.copying(config({ contentProtection: undefined }))).toBe(
      'Copying and printing turned off',
    );
  });

  it('says how many times a candidate may leave the page, or that the page is not watched', () => {
    expect(summary.leaving(config({ focusViolationLimit: 0 }))).toBe('Not watched');
    expect(summary.leaving(config({ focusViolationLimit: 1 }))).toBe('Ends after 1 time');
    expect(summary.leaving(config({ focusViolationLimit: 3 }))).toBe('Ends after 3 times');
    expect(summary.leaving(config({ focusViolationLimit: undefined }))).toBe('Not watched');
  });
});
