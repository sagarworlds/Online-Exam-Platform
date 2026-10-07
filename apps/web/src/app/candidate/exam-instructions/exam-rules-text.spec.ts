import { ExamRulesDto, MyExamDto } from '../candidate.models';
import { TestBed } from '@angular/core/testing';
import { I18nService } from '../../i18n/i18n.service';
import { instructionLines, markingRules, marks, timeAllowed } from './exam-rules-text';

const RULES: ExamRulesDto = {
  correctMarks: 4,
  incorrectMarks: -1,
  unattemptedMarks: 0,
  partialCredit: false,
  sectionLock: false,
  sectionCount: 1,
};

function exam(overrides: Partial<MyExamDto> = {}): MyExamDto {
  return {
    examId: 'e1',
    name: 'Maths',
    description: null,
    startUtc: '2026-10-05T04:30:00Z',
    endUtc: '2026-10-05T07:30:00Z',
    lateEntryDeadlineUtc: null,
    durationSeconds: 3600,
    questionCount: 10,
    state: 'Open',
    attemptId: null,
    attemptStatus: null,
    score: null,
    maxScore: null,
    attemptsAllowed: 1,
    attemptsUsed: 0,
    canStartAttempt: true,
    attempts: [],
    canRequestAttempt: false,
    attemptRequest: null,
    rules: RULES,
    ...overrides,
  };
}

describe('marks', () => {
  it('says "mark" for one and "marks" otherwise, ignoring the sign', () => {
    expect(marks(1)).toBe('1 mark');
    expect(marks(-1)).toBe('1 mark');
    expect(marks(4)).toBe('4 marks');
    expect(marks(0.5)).toBe('0.5 marks');
  });
});

describe('markingRules', () => {
  it('says plainly what a correct, a wrong and an unanswered question do', () => {
    expect(markingRules(RULES)).toEqual([
      'Each correct answer earns 4 marks.',
      'A wrong answer costs 1 mark.',
      'A question you leave unanswered earns no marks.',
    ]);
  });

  it('says a wrong answer earns no marks when there is no negative marking', () => {
    expect(markingRules({ ...RULES, incorrectMarks: 0 })[1]).toBe('A wrong answer earns no marks.');
  });

  it('handles an unusual scheme where wrong or unanswered questions earn marks', () => {
    const lines = markingRules({ ...RULES, incorrectMarks: 0.5, unattemptedMarks: -2 });

    expect(lines[1]).toBe('A wrong answer earns 0.5 marks.');
    expect(lines[2]).toBe('A question you leave unanswered costs 2 marks.');
  });

  it('adds a line about partial credit only when it is on', () => {
    expect(markingRules(RULES)).toHaveLength(3);
    expect(markingRules({ ...RULES, partialCredit: true }).at(-1)).toContain('part of its marks');
  });
});

describe('timeAllowed', () => {
  it('states minutes, or that the exam runs until it closes', () => {
    expect(timeAllowed({ durationSeconds: 5400 })).toBe('90 minutes');
    expect(timeAllowed({ durationSeconds: 60 })).toBe('1 minute');
    expect(timeAllowed({ durationSeconds: null })).toBe('until the exam closes');
  });
});

describe('instructionLines', () => {
  it('opens with the time allowed and that the server keeps the clock', () => {
    const lines = instructionLines(exam());

    expect(lines[0]).toBe('You have 60 minutes from the moment you press Start exam.');
    expect(lines[1]).toContain('runs on the server');
  });

  it('says the work runs until the exam closes when there is no duration', () => {
    expect(instructionLines(exam({ durationSeconds: null }))[0]).toContain('until the exam closes');
  });

  it('states the exam\'s own marking scheme', () => {
    const lines = instructionLines(exam());

    expect(lines).toContain('Each correct answer earns 4 marks.');
    expect(lines).toContain('A wrong answer costs 1 mark.');
  });

  it('warns that a locked section cannot be returned to, and otherwise says movement is free', () => {
    const locked = instructionLines(exam({ rules: { ...RULES, sectionLock: true, sectionCount: 3 } }));
    expect(locked.some((l) => l.includes('3 sections') && l.includes('cannot come back'))).toBe(true);

    expect(instructionLines(exam())).toContain('You can move freely between questions.');
  });

  it('mentions attempts only when there is more than one', () => {
    expect(instructionLines(exam()).some((l) => l.includes('attempts'))).toBe(false);

    const lines = instructionLines(exam({ attemptsAllowed: 3, attemptsUsed: 1 }));
    expect(lines.at(-1)).toBe('You have 3 attempts at this exam in all, and have used 1. This starts attempt 2.');
  });

  it('shows the proctoring notice the server wrote, in its own words, after the rules', () => {
    const notice = ['Your IP address is recorded.', 'Copying is turned off.', 'No camera is used.'];

    const lines = instructionLines(exam({ rules: { ...RULES, proctoringNotice: notice } }));

    expect(lines).toEqual(expect.arrayContaining(notice));
    expect(lines.indexOf('Your IP address is recorded.')).toBeGreaterThan(lines.findIndex((l) => l.includes('earns')));
    expect(lines.indexOf('No camera is used.')).toBe(lines.indexOf('Your IP address is recorded.') + 2);
  });

  it('words nothing about proctoring itself, so the notice cannot disagree with what is collected', () => {
    const lines = instructionLines(exam({ rules: { ...RULES, contentProtection: true, focusViolationLimit: 3 } }));

    expect(lines.some((l) => /copying|IP address|full screen/i.test(l))).toBe(false);
  });

  it('shows no proctoring lines when the API sent none', () => {
    expect(instructionLines(exam({ rules: { ...RULES } })).some((l) => /recorded|camera/i.test(l))).toBe(false);
    expect(instructionLines(exam({ rules: null })).some((l) => /recorded|camera/i.test(l))).toBe(false);
  });

  describe('in another language (FR-51)', () => {
    afterEach(() => localStorage.clear());

    const inLanguage = (language: 'hi' | 'mr') => {
      localStorage.clear();
      const i18n = TestBed.inject(I18nService);
      i18n.setLanguage(language);
      return i18n;
    };

    it('states the marking in Hindi, with the amounts filled in', () => {
      const words = inLanguage('hi');

      expect(markingRules(RULES, words)).toEqual([
        'हर सही उत्तर पर 4 अंक मिलते हैं।',
        'गलत उत्तर पर 1 अंक कटते हैं।',
        'जिस प्रश्न का उत्तर आप खाली छोड़ते हैं उस पर कोई अंक नहीं मिलते।',
      ]);
    });

    it('states the time and the attempts in Marathi', () => {
      const words = inLanguage('mr');

      expect(timeAllowed(exam({ durationSeconds: 5400 }), words)).toBe('90 मिनिटे');
      expect(timeAllowed(exam({ durationSeconds: null }), words)).toBe('परीक्षा बंद होईपर्यंत');
      expect(instructionLines(exam({ attemptsAllowed: 3, attemptsUsed: 1 }), words).at(-1)).toContain('3');
    });

    it('chooses the singular form of a section count by the count', () => {
      const words = inLanguage('hi');

      const lines = instructionLines(exam({ rules: { ...RULES, sectionLock: true, sectionCount: 1 } }), words);

      expect(lines.some((l) => l.includes('1 खंड है'))).toBe(true);
    });

    it('is the same sentences as the English ones in number, so no rule is lost in translation', () => {
      const english = instructionLines(exam({ rules: { ...RULES, partialCredit: true, sectionLock: true, sectionCount: 3 }, attemptsAllowed: 2 }));

      expect(instructionLines(exam({ rules: { ...RULES, partialCredit: true, sectionLock: true, sectionCount: 3 }, attemptsAllowed: 2 }), inLanguage('hi')))
        .toHaveLength(english.length);
      expect(instructionLines(exam({ rules: { ...RULES, partialCredit: true, sectionLock: true, sectionCount: 3 }, attemptsAllowed: 2 }), inLanguage('mr')))
        .toHaveLength(english.length);
    });
  });

  it('still gives the general rules when the API sent no exam rules', () => {
    const lines = instructionLines(exam({ rules: null }));

    expect(lines.some((l) => l.includes('earns'))).toBe(false);
    expect(lines.some((l) => l.includes('Stay on this device'))).toBe(true);
  });
});
