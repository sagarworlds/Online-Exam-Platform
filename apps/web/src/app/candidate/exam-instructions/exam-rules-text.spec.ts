import { ExamRulesDto, MyExamDto } from '../candidate.models';
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

  it('tells the candidate up front that copying, pasting, right-click and printing are off, unless the author lifted that', () => {
    const told = 'Copying, pasting, right-click and printing are turned off during the exam. You can still select text.';

    expect(instructionLines(exam())).toContain(told);
    expect(instructionLines(exam({ rules: { ...RULES, contentProtection: true } }))).toContain(told);
    expect(instructionLines(exam({ rules: { ...RULES, contentProtection: false } }))).not.toContain(told);
  });

  it('tells the candidate up front that leaving the exam page is recorded, naming the limit', () => {
    const lines = (limit?: number) => instructionLines(exam({ rules: { ...RULES, focusViolationLimit: limit } })).join(' ');

    expect(lines(3)).toContain('If you leave 3 times, the exam is submitted for you with the answers saved so far.');
    expect(lines(1)).toContain('The exam is submitted for you the first time you leave.');
    expect(lines(0)).not.toContain('Stay on the exam page');
    expect(lines(undefined)).not.toContain('Stay on the exam page');
  });

  it('tells every candidate that their address and device are recorded, whatever the exam does', () => {
    const told = 'Your IP address and a signature of your device and browser are recorded while you sit the exam, and kept with your attempt for the organisers.';

    expect(instructionLines(exam())).toContain(told);
    expect(instructionLines(exam({ rules: null }))).toContain(told);
  });

  it('still gives the general rules when the API sent no exam rules', () => {
    const lines = instructionLines(exam({ rules: null }));

    expect(lines.some((l) => l.includes('earns'))).toBe(false);
    expect(lines.some((l) => l.includes('Stay on this device'))).toBe(true);
  });
});
