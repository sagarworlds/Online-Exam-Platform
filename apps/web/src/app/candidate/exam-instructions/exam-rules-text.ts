import { ExamRulesDto, MyExamDto } from '../candidate.models';

/** "1 mark", "2 marks", "0.5 marks": the number as the exam sets it, without a spurious ".0". */
export function marks(value: number): string {
  const amount = Math.abs(value);
  return `${amount} ${amount === 1 ? 'mark' : 'marks'}`;
}

/** What a wrong or unanswered question does to the score, in a sentence that says so plainly. */
function effect(subject: string, value: number): string {
  if (value === 0) {
    return `${subject} earns no marks.`;
  }

  return value < 0 ? `${subject} costs ${marks(value)}.` : `${subject} earns ${marks(value)}.`;
}

/** The sentences that explain how an exam is marked. */
export function markingRules(rules: ExamRulesDto): string[] {
  const lines = [
    `Each correct answer earns ${marks(rules.correctMarks)}.`,
    effect('A wrong answer', rules.incorrectMarks),
    effect('A question you leave unanswered', rules.unattemptedMarks),
  ];

  if (rules.partialCredit) {
    lines.push('A question with several correct options can earn part of its marks if you choose some of them.');
  }

  return lines;
}

/** How long the candidate has, in the words the instructions use. */
export function timeAllowed(exam: Pick<MyExamDto, 'durationSeconds'>): string {
  if (exam.durationSeconds === null) {
    return 'until the exam closes';
  }

  const minutes = Math.round(exam.durationSeconds / 60);
  return minutes === 1 ? '1 minute' : `${minutes} minutes`;
}

/**
 * Everything a candidate is told before starting, as short sentences in the order they matter. Only rules that are true of this
 * exam are stated: marking and section lock come from the exam's own settings.
 */
export function instructionLines(exam: MyExamDto): string[] {
  const lines: string[] = [];

  lines.push(
    exam.durationSeconds === null
      ? 'You can work until the exam closes. The timer starts when you press Start exam.'
      : `You have ${timeAllowed(exam)} from the moment you press Start exam.`,
  );
  lines.push(
    'The timer runs on the server, not on your device. It keeps running if you refresh the page, close it or lose your connection, ' +
      'and the exam is submitted for you with the answers saved so far when time runs out.',
  );
  lines.push('Each answer is saved as you choose it. You can change or clear it, and mark a question for review, until you submit or time runs out.');

  if (exam.rules !== null) {
    lines.push(...markingRules(exam.rules));
    lines.push(
      exam.rules.sectionLock
        ? `The exam has ${exam.rules.sectionCount} ${exam.rules.sectionCount === 1 ? 'section' : 'sections'}, taken in order. Once you move on from a section you cannot come back to it.`
        : 'You can move freely between questions.',
    );
  }

  // Told up front, so a refusal during the exam is never a surprise. On unless the author lifted it (older APIs do not say).
  if (exam.rules !== null && exam.rules.contentProtection !== false) {
    lines.push('Copying, pasting, right-click and printing are turned off during the exam. You can still select text.');
  }

  // The limit is the exam's, so the sentence names it; an exam that does not watch (older APIs do not say) says nothing.
  const limit = exam.rules?.focusViolationLimit ?? 0;
  if (limit > 0) {
    lines.push(
      'Stay on the exam page and in full screen. Switching to another tab or window, or leaving full screen, is recorded and you are warned each time. ' +
        (limit === 1
          ? 'The exam is submitted for you the first time you leave.'
          : `If you leave ${limit} times, the exam is submitted for you with the answers saved so far.`),
    );
  }

  // Always true, so always said: where the candidate sits the exam is recorded (FR-26).
  lines.push('Your IP address and a signature of your device and browser are recorded while you sit the exam, and kept with your attempt for the organisers.');

  lines.push('Stay on this device and browser. Signing in somewhere else ends this session.');
  lines.push('You can submit before time runs out. Once you submit, your answers cannot be changed.');

  if (exam.attemptsAllowed > 1) {
    lines.push(`You have ${exam.attemptsAllowed} attempts at this exam in all, and have used ${exam.attemptsUsed}. This starts attempt ${exam.attemptsUsed + 1}.`);
  }

  return lines;
}
